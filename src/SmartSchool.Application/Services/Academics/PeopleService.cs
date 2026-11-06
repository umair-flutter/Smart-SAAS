using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

/// <summary>Teachers, students and parents/guardians of the active school.</summary>
public class PeopleService(IAppDbContext db, SchoolContext ctx, AccountLinker accounts, StatusOptionService statuses)
{
    // ---------- Teachers ----------

    public async Task<PagedResult<TeacherDto>> ListTeachersAsync(PagedQuery query, CancellationToken ct)
    {
        var classes = await ctx.ClassesInActiveYearAsync(ct);
        var q = db.Teachers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(t => t.FirstName.Contains(s) || t.LastName.Contains(s) || (t.Email != null && t.Email.Contains(s)) || (t.EmployeeNo != null && t.EmployeeNo.Contains(s)));
        }
        q = query.SortBy?.ToLowerInvariant() switch
        {
            "specialty" => q.OrderByDirection(t => t.Specialty, query.SortDesc),
            "hired" => q.OrderByDirection(t => t.HireDate, query.SortDesc),
            _ => q.OrderByDirection(t => t.LastName, query.SortDesc).ThenBy(t => t.FirstName)
        };
        return await q.Select(t => new TeacherDto
        {
            Id = t.Id,
            FirstName = t.FirstName,
            LastName = t.LastName,
            Email = t.Email,
            Phone = t.Phone,
            EmployeeNo = t.EmployeeNo,
            Specialty = t.Specialty,
            HireDate = t.HireDate,
            IsActive = t.IsActive,
            HasLogin = t.UserId != null,
            Status = t.StatusOption == null ? null : new StatusBadgeDto(t.StatusOption.Id, t.StatusOption.Name, t.StatusOption.Color),
            Subjects = db.TeachingAssignments.Where(a => a.TeacherId == t.Id && classes.Any(c => c.Id == a.ClassId))
                .Select(a => a.Subject!.Name).Distinct().ToList(),
            ClassCount = db.TeachingAssignments.Where(a => a.TeacherId == t.Id && classes.Any(c => c.Id == a.ClassId))
                .Select(a => a.ClassId).Distinct().Count()
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<TeacherEditDto> GetTeacherAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Teachers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Teacher", id);
        return new TeacherEditDto
        {
            FirstName = t.FirstName, LastName = t.LastName, Email = t.Email, Phone = t.Phone, Address = t.Address,
            IsActive = t.IsActive, EmployeeNo = t.EmployeeNo, Specialty = t.Specialty, HireDate = t.HireDate, CreateLogin = t.UserId != null, StatusOptionId = t.StatusOptionId
        };
    }

    public async Task<Guid> SaveTeacherAsync(Guid? id, TeacherEditDto dto, CancellationToken ct)
    {
        var teacher = id is { } existing
            ? await db.Teachers.FirstOrDefaultAsync(t => t.Id == existing, ct) ?? throw new NotFoundException("Teacher", existing)
            : new Teacher { SchoolId = await ctx.RequireSchoolAsync(ct) };

        await statuses.EnsureValidAsync(dto.StatusOptionId, StatusCategory.Teacher, ct);
        if (teacher.UserId is null)
            teacher.UserId = await accounts.CreateLoginAsync(RoleType.Teacher, dto, teacher.SchoolId, ct);
        ApplyPerson(teacher, dto);
        teacher.EmployeeNo = dto.EmployeeNo?.Trim();
        teacher.Specialty = dto.Specialty?.Trim();
        teacher.HireDate = dto.HireDate;

        if (id is null) db.Teachers.Add(teacher);
        await db.SaveChangesAsync(ct);
        return teacher.Id;
    }

    public async Task DeleteTeacherAsync(Guid id, CancellationToken ct)
    {
        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Teacher", id);
        if (await db.TeachingAssignments.AnyAsync(a => a.TeacherId == id, ct) || await db.Classes.AnyAsync(c => c.HomeroomTeacherId == id, ct))
            throw new ConflictException("This teacher has class assignments. Remove them or mark the teacher inactive.");
        db.Teachers.Remove(teacher);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Students ----------

    public async Task<PagedResult<StudentDto>> ListStudentsAsync(PagedQuery query, Guid? classId, CancellationToken ct)
    {
        var year = await ctx.ResolveYearAsync(ct);
        var q = db.Students.AsNoTracking();
        if (classId is { } cid)
            q = q.Where(s => s.Enrollments.Any(e => e.ClassId == cid && !e.IsDeleted));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.FirstName.Contains(term) || s.LastName.Contains(term) || s.StudentNo.Contains(term));
        }
        q = query.SortBy?.ToLowerInvariant() switch
        {
            "no" => q.OrderByDirection(s => s.StudentNo, query.SortDesc),
            "dob" => q.OrderByDirection(s => s.DateOfBirth, query.SortDesc),
            _ => q.OrderByDirection(s => s.LastName, query.SortDesc).ThenBy(s => s.FirstName)
        };

        return await q.Select(s => new StudentDto
        {
            Id = s.Id,
            StudentNo = s.StudentNo,
            FirstName = s.FirstName,
            LastName = s.LastName,
            DateOfBirth = s.DateOfBirth,
            Gender = s.Gender,
            Email = s.Email,
            IsActive = s.IsActive,
            HasLogin = s.UserId != null,
            Status = s.StatusOption == null ? null : new StatusBadgeDto(s.StatusOption.Id, s.StatusOption.Name, s.StatusOption.Color),
            ClassId = s.Enrollments.Where(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active && (year == null ? e.Class!.AcademicYear!.IsCurrent : e.AcademicYearId == year))
                .Select(e => (Guid?)e.ClassId).FirstOrDefault(),
            ClassName = s.Enrollments.Where(e => !e.IsDeleted && e.Status == EnrollmentStatus.Active && (year == null ? e.Class!.AcademicYear!.IsCurrent : e.AcademicYearId == year))
                .Select(e => e.Class!.Name).FirstOrDefault(),
            Guardians = s.Guardians.Select(g => g.Guardian!.FirstName + " " + g.Guardian.LastName).ToList()
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<StudentDetailDto> GetStudentAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking()
            .Include(x => x.Guardians).ThenInclude(g => g.Guardian)
            .Include(x => x.StatusOption)
            .Include(x => x.Enrollments).ThenInclude(e => e.Class).ThenInclude(c => c!.AcademicYear)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);

        var enrollments = s.Enrollments.Where(e => e.Class is not null)
            .OrderByDescending(e => e.Class!.AcademicYear!.StartDate)
            .Select(e => new EnrollmentDto
            {
                Id = e.Id, AcademicYearId = e.AcademicYearId, AcademicYearName = e.Class!.AcademicYear!.Name,
                ClassId = e.ClassId, ClassName = e.Class.Name, Status = e.Status, EnrolledOn = e.EnrolledOn
            }).ToList();
        var active = s.Enrollments.Where(e => e.Status == EnrollmentStatus.Active && e.Class?.AcademicYear?.IsCurrent == true).FirstOrDefault();

        return new StudentDetailDto
        {
            Id = s.Id, StudentNo = s.StudentNo, FirstName = s.FirstName, LastName = s.LastName, DateOfBirth = s.DateOfBirth,
            Gender = s.Gender, Email = s.Email, Phone = s.Phone, Address = s.Address, AdmissionDate = s.AdmissionDate,
            IsActive = s.IsActive, HasLogin = s.UserId != null,
            Status = s.StatusOption == null ? null : new StatusBadgeDto(s.StatusOption.Id, s.StatusOption.Name, s.StatusOption.Color),
            ClassId = active?.ClassId, ClassName = active?.Class?.Name,
            Guardians = s.Guardians.Where(g => g.Guardian is not null).Select(g => g.Guardian!.FullName).ToList(),
            GuardianLinks = s.Guardians.Where(g => g.Guardian is not null).OrderByDescending(g => g.IsPrimary).Select(g => new StudentGuardianDto
            {
                GuardianId = g.GuardianId, FullName = g.Guardian!.FullName, Phone = g.Guardian.Phone, Email = g.Guardian.Email,
                Relationship = g.Relationship, IsPrimary = g.IsPrimary
            }).ToList(),
            Enrollments = enrollments
        };
    }

    public async Task<StudentEditDto> GetStudentForEditAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Student", id);
        return new StudentEditDto
        {
            StudentNo = s.StudentNo, FirstName = s.FirstName, LastName = s.LastName, Email = s.Email, Phone = s.Phone, Address = s.Address,
            IsActive = s.IsActive, DateOfBirth = s.DateOfBirth, Gender = s.Gender, AdmissionDate = s.AdmissionDate, CreateLogin = s.UserId != null, StatusOptionId = s.StatusOptionId
        };
    }

    public async Task<Guid> SaveStudentAsync(Guid? id, StudentEditDto dto, CancellationToken ct)
    {
        var student = id is { } existing
            ? await db.Students.FirstOrDefaultAsync(s => s.Id == existing, ct) ?? throw new NotFoundException("Student", existing)
            : new Student { SchoolId = await ctx.RequireSchoolAsync(ct), AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow) };

        var number = string.IsNullOrWhiteSpace(dto.StudentNo) ? student.StudentNo : dto.StudentNo.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(number)) number = await NextStudentNoAsync(student.SchoolId, ct);
        if (await db.Students.AnyAsync(s => s.SchoolId == student.SchoolId && s.StudentNo == number && s.Id != student.Id, ct))
            throw new ValidationException(nameof(dto.StudentNo), "Another student already has this number.");

        SchoolClass? enrollIn = null;
        if (id is null && dto.ClassId is { } classId)
            enrollIn = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId && c.SchoolId == student.SchoolId, ct)
                ?? throw new ValidationException(nameof(dto.ClassId), "Select a class of this school.");

        await statuses.EnsureValidAsync(dto.StatusOptionId, StatusCategory.Student, ct);
        if (student.UserId is null)
            student.UserId = await accounts.CreateLoginAsync(RoleType.Student, dto, student.SchoolId, ct);
        ApplyPerson(student, dto);
        student.StudentNo = number;
        student.DateOfBirth = dto.DateOfBirth;
        student.Gender = dto.Gender;
        if (dto.AdmissionDate is not null) student.AdmissionDate = dto.AdmissionDate;

        if (id is null) db.Students.Add(student);
        if (enrollIn is not null)
            db.Enrollments.Add(new Enrollment
            {
                SchoolId = student.SchoolId, StudentId = student.Id, ClassId = enrollIn.Id, AcademicYearId = enrollIn.AcademicYearId,
                EnrolledOn = DateOnly.FromDateTime(DateTime.UtcNow)
            });
        await db.SaveChangesAsync(ct);
        return student.Id;
    }

    public async Task DeleteStudentAsync(Guid id, CancellationToken ct)
    {
        var student = await db.Students.Include(s => s.Enrollments).Include(s => s.Guardians).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Student", id);
        foreach (var e in student.Enrollments) db.Enrollments.Remove(e);
        db.StudentGuardians.RemoveRange(student.Guardians);
        db.Students.Remove(student);
        await db.SaveChangesAsync(ct);
    }

    public async Task LinkGuardianAsync(Guid studentId, LinkGuardianDto dto, CancellationToken ct)
    {
        var student = await db.Students.Include(s => s.Guardians).FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Student", studentId);
        if (!await db.Guardians.AnyAsync(g => g.Id == dto.GuardianId && g.SchoolId == student.SchoolId, ct))
            throw new ValidationException(nameof(dto.GuardianId), "Select a parent or guardian of this school.");

        if (dto.IsPrimary) student.Guardians.ForEach(g => g.IsPrimary = false);
        var link = student.Guardians.FirstOrDefault(g => g.GuardianId == dto.GuardianId);
        if (link is null)
        {
            link = new StudentGuardian { StudentId = studentId, GuardianId = dto.GuardianId!.Value };
            student.Guardians.Add(link);
        }
        link.Relationship = dto.Relationship.Trim();
        link.IsPrimary = dto.IsPrimary || student.Guardians.Count == 1;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkGuardianAsync(Guid studentId, Guid guardianId, CancellationToken ct)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student", studentId);
        var link = await db.StudentGuardians.FirstOrDefaultAsync(x => x.StudentId == studentId && x.GuardianId == guardianId, ct);
        if (link is null) return;
        db.StudentGuardians.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> NextStudentNoAsync(Guid schoolId, CancellationToken ct)
    {
        var prefix = $"S{DateTime.UtcNow:yy}";
        var count = await db.Students.IgnoreQueryFilters().CountAsync(s => s.SchoolId == schoolId && s.StudentNo.StartsWith(prefix), ct);
        for (var n = count + 1; ; n++)
        {
            var candidate = $"{prefix}{n:D4}";
            if (!await db.Students.IgnoreQueryFilters().AnyAsync(s => s.SchoolId == schoolId && s.StudentNo == candidate, ct))
                return candidate;
        }
    }

    // ---------- Guardians ----------

    public async Task<PagedResult<GuardianDto>> ListGuardiansAsync(PagedQuery query, CancellationToken ct)
    {
        var q = db.Guardians.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(g => g.FirstName.Contains(s) || g.LastName.Contains(s) || (g.Email != null && g.Email.Contains(s)) || (g.Phone != null && g.Phone.Contains(s)));
        }
        q = q.OrderByDirection(g => g.LastName, query.SortDesc).ThenBy(g => g.FirstName);
        return await q.Select(g => new GuardianDto
        {
            Id = g.Id,
            FirstName = g.FirstName,
            LastName = g.LastName,
            Email = g.Email,
            Phone = g.Phone,
            Occupation = g.Occupation,
            IsActive = g.IsActive,
            HasLogin = g.UserId != null,
            Status = g.StatusOption == null ? null : new StatusBadgeDto(g.StatusOption.Id, g.StatusOption.Name, g.StatusOption.Color),
            Children = g.Students.Where(x => !x.Student!.IsDeleted).Select(x => x.Student!.FirstName + " " + x.Student.LastName).ToList()
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<GuardianEditDto> GetGuardianAsync(Guid id, CancellationToken ct)
    {
        var g = await db.Guardians.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Guardian", id);
        return new GuardianEditDto
        {
            FirstName = g.FirstName, LastName = g.LastName, Email = g.Email, Phone = g.Phone, Address = g.Address,
            IsActive = g.IsActive, Occupation = g.Occupation, CreateLogin = g.UserId != null, StatusOptionId = g.StatusOptionId
        };
    }

    public async Task<Guid> SaveGuardianAsync(Guid? id, GuardianEditDto dto, CancellationToken ct)
    {
        var guardian = id is { } existing
            ? await db.Guardians.FirstOrDefaultAsync(g => g.Id == existing, ct) ?? throw new NotFoundException("Guardian", existing)
            : new Guardian { SchoolId = await ctx.RequireSchoolAsync(ct) };

        await statuses.EnsureValidAsync(dto.StatusOptionId, StatusCategory.Guardian, ct);
        if (guardian.UserId is null)
            guardian.UserId = await accounts.CreateLoginAsync(RoleType.Parent, dto, guardian.SchoolId, ct);
        ApplyPerson(guardian, dto);
        guardian.Occupation = dto.Occupation?.Trim();

        if (id is null) db.Guardians.Add(guardian);
        await db.SaveChangesAsync(ct);
        return guardian.Id;
    }

    public async Task DeleteGuardianAsync(Guid id, CancellationToken ct)
    {
        var guardian = await db.Guardians.Include(g => g.Students).FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new NotFoundException("Guardian", id);
        db.StudentGuardians.RemoveRange(guardian.Students);
        db.Guardians.Remove(guardian);
        await db.SaveChangesAsync(ct);
    }

    private static void ApplyPerson(Person p, PersonEditDto dto)
    {
        p.FirstName = dto.FirstName.Trim();
        p.LastName = dto.LastName.Trim();
        p.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        p.Phone = dto.Phone?.Trim();
        p.Address = dto.Address?.Trim();
        p.IsActive = dto.IsActive;
        p.StatusOptionId = dto.StatusOptionId;
    }
}
