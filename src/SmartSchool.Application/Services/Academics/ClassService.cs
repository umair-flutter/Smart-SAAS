using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

public class ClassService(IAppDbContext db, SchoolContext ctx)
{
    public async Task<List<ClassDto>> ListAsync(CancellationToken ct)
    {
        var classes = await ctx.ClassesInActiveYearAsync(ct);
        return await Project(classes.AsNoTracking().OrderBy(c => c.Level).ThenBy(c => c.Name)).ToListAsync(ct);
    }

    public async Task<ClassDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await db.Classes.AsNoTracking().Where(c => c.Id == id).Select(c => new ClassDetailDto
        {
            Id = c.Id,
            Name = c.Name,
            Level = c.Level,
            AcademicYearId = c.AcademicYearId,
            AcademicYearName = c.AcademicYear!.Name,
            Capacity = c.Capacity,
            StudentCount = c.Enrollments.Count(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active),
            HomeroomTeacherId = c.HomeroomTeacherId,
            HomeroomTeacherName = c.HomeroomTeacher != null ? c.HomeroomTeacher.FirstName + " " + c.HomeroomTeacher.LastName : null,
            RoomId = c.RoomId,
            RoomName = c.Room != null ? c.Room.Code + " · " + c.Room.Name : null,
            Students = c.Enrollments.Where(e => !e.IsDeleted && !e.Student!.IsDeleted)
                .OrderBy(e => e.Student!.LastName).ThenBy(e => e.Student!.FirstName)
                .Select(e => new ClassStudentDto
                {
                    EnrollmentId = e.Id, StudentId = e.StudentId, StudentNo = e.Student!.StudentNo,
                    FullName = e.Student.FirstName + " " + e.Student.LastName, Status = e.Status, EnrolledOn = e.EnrolledOn
                }).ToList(),
            Assignments = c.Assignments.Where(a => !a.IsDeleted).OrderBy(a => a.Subject!.Name).Select(a => new AssignmentDto
            {
                Id = a.Id, ClassId = c.Id, ClassName = c.Name, SubjectId = a.SubjectId, SubjectName = a.Subject!.Name,
                TeacherId = a.TeacherId, TeacherName = a.Teacher!.FirstName + " " + a.Teacher.LastName, HoursPerWeek = a.HoursPerWeek
            }).ToList()
        }).FirstOrDefaultAsync(ct);
        return dto ?? throw new NotFoundException("Class", id);
    }

    public async Task<Guid> SaveAsync(Guid? id, ClassEditDto dto, CancellationToken ct)
    {
        SchoolClass cls;
        if (id is { } existing)
            cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == existing, ct) ?? throw new NotFoundException("Class", existing);
        else
        {
            var schoolId = await ctx.RequireSchoolAsync(ct);
            var yearId = dto.AcademicYearId ?? await ctx.RequireYearAsync(ct);
            if (!await db.AcademicYears.AnyAsync(y => y.Id == yearId && y.SchoolId == schoolId, ct))
                throw new ValidationException(nameof(dto.AcademicYearId), "Select an academic year of this school.");
            cls = new SchoolClass { SchoolId = schoolId, AcademicYearId = yearId };
            db.Classes.Add(cls);
        }

        var name = dto.Name.Trim();
        if (await db.Classes.AnyAsync(c => c.SchoolId == cls.SchoolId && c.AcademicYearId == cls.AcademicYearId && c.Name == name && c.Id != cls.Id, ct))
            throw new ValidationException(nameof(dto.Name), "Another class in this academic year already has this name.");
        if (dto.HomeroomTeacherId is { } tid && !await db.Teachers.AnyAsync(t => t.Id == tid && t.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.HomeroomTeacherId), "Select a teacher of this school.");
        if (dto.RoomId is { } rid && !await db.Rooms.AnyAsync(r => r.Id == rid && r.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.RoomId), "Select a room of this school.");
        if (id is not null)
        {
            var enrolled = await db.Enrollments.CountAsync(e => e.ClassId == cls.Id && e.Status == EnrollmentStatus.Active, ct);
            if (dto.Capacity < enrolled)
                throw new ValidationException(nameof(dto.Capacity), $"{enrolled} students are already enrolled; the capacity cannot be lower.");
        }

        cls.Name = name;
        cls.Level = dto.Level?.Trim();
        cls.Capacity = dto.Capacity;
        cls.HomeroomTeacherId = dto.HomeroomTeacherId;
        cls.RoomId = dto.RoomId;
        await db.SaveChangesAsync(ct);
        return cls.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Class", id);
        if (await db.Enrollments.AnyAsync(e => e.ClassId == id, ct))
            throw new ConflictException("Move or withdraw the enrolled students before deleting this class.");
        foreach (var a in await db.TeachingAssignments.Where(a => a.ClassId == id).ToListAsync(ct)) db.TeachingAssignments.Remove(a);
        foreach (var s in await db.TimetableSlots.Where(s => s.ClassId == id).ToListAsync(ct)) db.TimetableSlots.Remove(s);
        db.Classes.Remove(cls);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Enrollments ----------

    /// <summary>Enrols students into the class. A student already in another class of the same year is moved.</summary>
    public async Task<int> EnrollAsync(Guid classId, List<Guid> studentIds, CancellationToken ct)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        var ids = studentIds.Distinct().ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id) && s.SchoolId == cls.SchoolId).Select(s => s.Id).ToListAsync(ct);
        if (students.Count != ids.Count)
            throw new ValidationException("studentIds", "Some students do not belong to this school.");

        var existing = await db.Enrollments.Where(e => ids.Contains(e.StudentId) && e.AcademicYearId == cls.AcademicYearId).ToListAsync(ct);
        var active = await db.Enrollments.CountAsync(e => e.ClassId == classId && e.Status == EnrollmentStatus.Active, ct);
        var added = ids.Count(id => existing.All(e => e.StudentId != id || e.ClassId != classId || e.Status != EnrollmentStatus.Active));
        if (active + added > cls.Capacity)
            throw new ValidationException("capacity", $"This class has room for {Math.Max(0, cls.Capacity - active)} more student(s).");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var studentId in ids)
        {
            var e = existing.FirstOrDefault(x => x.StudentId == studentId);
            if (e is null)
                db.Enrollments.Add(new Enrollment
                {
                    SchoolId = cls.SchoolId, StudentId = studentId, ClassId = classId, AcademicYearId = cls.AcademicYearId, EnrolledOn = today
                });
            else
            {
                e.ClassId = classId;
                e.Status = EnrollmentStatus.Active;
                e.EnrolledOn = today;
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task UpdateEnrollmentAsync(Guid enrollmentId, EnrollmentStatusDto dto, CancellationToken ct)
    {
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct) ?? throw new NotFoundException("Enrollment", enrollmentId);
        if (dto.NewClassId is { } target && target != e.ClassId)
        {
            var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == target && c.AcademicYearId == e.AcademicYearId, ct)
                ?? throw new ValidationException(nameof(dto.NewClassId), "Select a class of the same academic year.");
            var count = await db.Enrollments.CountAsync(x => x.ClassId == target && x.Status == EnrollmentStatus.Active, ct);
            if (count >= cls.Capacity) throw new ValidationException(nameof(dto.NewClassId), $"{cls.Name} is full.");
            e.ClassId = target;
            e.Status = EnrollmentStatus.Active;
        }
        else e.Status = dto.Status;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveEnrollmentAsync(Guid enrollmentId, CancellationToken ct)
    {
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct) ?? throw new NotFoundException("Enrollment", enrollmentId);
        db.Enrollments.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Teacher / subject assignments ----------

    public async Task<Guid> SaveAssignmentAsync(Guid classId, Guid? assignmentId, AssignmentEditDto dto, CancellationToken ct)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class", classId);
        if (!await db.Subjects.AnyAsync(s => s.Id == dto.SubjectId && s.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.SubjectId), "Select a subject of this school.");
        if (!await db.Teachers.AnyAsync(t => t.Id == dto.TeacherId && t.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.TeacherId), "Select a teacher of this school.");

        TeachingAssignment a;
        if (assignmentId is { } aid)
            a = await db.TeachingAssignments.FirstOrDefaultAsync(x => x.Id == aid && x.ClassId == classId, ct) ?? throw new NotFoundException("Assignment", aid);
        else
        {
            a = new TeachingAssignment { SchoolId = cls.SchoolId, ClassId = classId };
            db.TeachingAssignments.Add(a);
        }
        if (await db.TeachingAssignments.AnyAsync(x => x.ClassId == classId && x.SubjectId == dto.SubjectId && x.Id != a.Id, ct))
            throw new ValidationException(nameof(dto.SubjectId), "This subject already has a teacher in this class.");

        a.SubjectId = dto.SubjectId!.Value;
        a.TeacherId = dto.TeacherId!.Value;
        a.HoursPerWeek = dto.HoursPerWeek;
        await db.SaveChangesAsync(ct);
        return a.Id;
    }

    public async Task DeleteAssignmentAsync(Guid classId, Guid assignmentId, CancellationToken ct)
    {
        var a = await db.TeachingAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.ClassId == classId, ct)
            ?? throw new NotFoundException("Assignment", assignmentId);
        db.TeachingAssignments.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    private static IQueryable<ClassDto> Project(IQueryable<SchoolClass> q) => q.Select(c => new ClassDto
    {
        Id = c.Id,
        Name = c.Name,
        Level = c.Level,
        AcademicYearId = c.AcademicYearId,
        AcademicYearName = c.AcademicYear!.Name,
        Capacity = c.Capacity,
        StudentCount = c.Enrollments.Count(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active),
        HomeroomTeacherId = c.HomeroomTeacherId,
        HomeroomTeacherName = c.HomeroomTeacher != null ? c.HomeroomTeacher.FirstName + " " + c.HomeroomTeacher.LastName : null,
        RoomId = c.RoomId,
        RoomName = c.Room != null ? c.Room.Code + " · " + c.Room.Name : null
    });
}

public class TimetableService(IAppDbContext db, SchoolContext ctx)
{
    /// <summary>Slots of one class, one teacher or one room in the active academic year.</summary>
    public async Task<List<TimetableSlotDto>> GetAsync(Guid? classId, Guid? teacherId, Guid? roomId, CancellationToken ct)
    {
        if (classId is null && teacherId is null && roomId is null)
            throw new ValidationException("filter", "Choose a class, a teacher or a room.");
        var classes = await ctx.ClassesInActiveYearAsync(ct);
        var q = db.TimetableSlots.AsNoTracking().Where(s => classes.Any(c => c.Id == s.ClassId));
        if (classId is not null) q = q.Where(s => s.ClassId == classId);
        if (teacherId is not null) q = q.Where(s => s.TeacherId == teacherId);
        if (roomId is not null) q = q.Where(s => s.RoomId == roomId);

        return await q.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).Select(s => new TimetableSlotDto
        {
            Id = s.Id,
            ClassId = s.ClassId,
            ClassName = s.Class!.Name,
            DayOfWeek = s.DayOfWeek,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            SubjectId = s.SubjectId,
            SubjectName = s.Subject!.Name,
            SubjectColor = s.Subject.Color,
            TeacherId = s.TeacherId,
            TeacherName = s.Teacher != null ? s.Teacher.FirstName + " " + s.Teacher.LastName : null,
            RoomId = s.RoomId,
            RoomName = s.Room != null ? s.Room.Code : null
        }).ToListAsync(ct);
    }

    public async Task<Guid> SaveAsync(Guid? id, TimetableSlotEditDto dto, CancellationToken ct)
    {
        if (dto.EndTime <= dto.StartTime)
            throw new ValidationException(nameof(dto.EndTime), "The lesson must end after it starts.");

        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == dto.ClassId, ct)
            ?? throw new ValidationException(nameof(dto.ClassId), "Select a class.");
        if (!await db.Subjects.AnyAsync(s => s.Id == dto.SubjectId && s.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.SubjectId), "Select a subject of this school.");
        if (dto.TeacherId is { } tid && !await db.Teachers.AnyAsync(t => t.Id == tid && t.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.TeacherId), "Select a teacher of this school.");
        if (dto.RoomId is { } rid && !await db.Rooms.AnyAsync(r => r.Id == rid && r.SchoolId == cls.SchoolId, ct))
            throw new ValidationException(nameof(dto.RoomId), "Select a room of this school.");

        // Default to the teacher assigned to this subject in this class, and to the class's own room.
        dto.TeacherId ??= await db.TeachingAssignments
            .Where(a => a.ClassId == cls.Id && a.SubjectId == dto.SubjectId).Select(a => (Guid?)a.TeacherId).FirstOrDefaultAsync(ct);
        dto.RoomId ??= cls.RoomId;

        // Overlapping lessons on the same day, in the same academic year.
        var overlapping = await db.TimetableSlots
            .Where(s => s.Id != id && s.DayOfWeek == dto.DayOfWeek && s.Class!.AcademicYearId == cls.AcademicYearId
                && s.StartTime < dto.EndTime && s.EndTime > dto.StartTime)
            .Select(s => new { s.ClassId, s.TeacherId, s.RoomId, Class = s.Class!.Name, s.StartTime, s.EndTime })
            .ToListAsync(ct);

        var clash = overlapping.FirstOrDefault(s => s.ClassId == cls.Id);
        if (clash is not null)
            throw new ConflictException($"{cls.Name} already has a lesson from {clash.StartTime:HH\\:mm} to {clash.EndTime:HH\\:mm}.");
        clash = dto.TeacherId is null ? null : overlapping.FirstOrDefault(s => s.TeacherId == dto.TeacherId);
        if (clash is not null)
            throw new ConflictException($"This teacher is already teaching {clash.Class} from {clash.StartTime:HH\\:mm} to {clash.EndTime:HH\\:mm}.");
        clash = dto.RoomId is null ? null : overlapping.FirstOrDefault(s => s.RoomId == dto.RoomId);
        if (clash is not null)
            throw new ConflictException($"This room is used by {clash.Class} from {clash.StartTime:HH\\:mm} to {clash.EndTime:HH\\:mm}.");

        TimetableSlot slot;
        if (id is { } existing)
            slot = await db.TimetableSlots.FirstOrDefaultAsync(s => s.Id == existing, ct) ?? throw new NotFoundException("Lesson", existing);
        else
        {
            slot = new TimetableSlot { SchoolId = cls.SchoolId };
            db.TimetableSlots.Add(slot);
        }
        slot.ClassId = cls.Id;
        slot.DayOfWeek = dto.DayOfWeek;
        slot.StartTime = dto.StartTime;
        slot.EndTime = dto.EndTime;
        slot.SubjectId = dto.SubjectId!.Value;
        slot.TeacherId = dto.TeacherId;
        slot.RoomId = dto.RoomId;
        await db.SaveChangesAsync(ct);
        return slot.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var slot = await db.TimetableSlots.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Lesson", id);
        db.TimetableSlots.Remove(slot);
        await db.SaveChangesAsync(ct);
    }
}
