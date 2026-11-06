using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Application.Services.Academics;

public class AcademicYearService(IAppDbContext db, SchoolContext ctx)
{
    public async Task<List<AcademicYearDto>> ListAsync(CancellationToken ct) =>
        await db.AcademicYears.AsNoTracking()
            .OrderByDescending(y => y.StartDate)
            .Select(y => new AcademicYearDto
            {
                Id = y.Id,
                Name = y.Name,
                StartDate = y.StartDate,
                EndDate = y.EndDate,
                IsCurrent = y.IsCurrent,
                ClassCount = db.Classes.Count(c => c.AcademicYearId == y.Id),
                StudentCount = db.Enrollments.Count(e => e.AcademicYearId == y.Id && e.Status == Shared.Enums.EnrollmentStatus.Active),
                Terms = y.Terms.Where(t => !t.IsDeleted).OrderBy(t => t.SortOrder)
                    .Select(t => new TermDto { Id = t.Id, Name = t.Name, StartDate = t.StartDate, EndDate = t.EndDate }).ToList()
            })
            .ToListAsync(ct);

    public async Task<Guid> CreateAsync(AcademicYearEditDto dto, CancellationToken ct)
    {
        var schoolId = await ctx.RequireSchoolAsync(ct);
        Validate(dto);
        if (await db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId && y.Name == dto.Name.Trim(), ct))
            throw new ValidationException(nameof(dto.Name), "This school already has an academic year with this name.");

        var isFirst = !await db.AcademicYears.AnyAsync(y => y.SchoolId == schoolId, ct);
        var year = new AcademicYear { SchoolId = schoolId };
        await Apply(year, dto, isFirst, ct);
        db.AcademicYears.Add(year);
        await db.SaveChangesAsync(ct);
        return year.Id;
    }

    public async Task UpdateAsync(Guid id, AcademicYearEditDto dto, CancellationToken ct)
    {
        Validate(dto);
        var year = await db.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, ct)
            ?? throw new NotFoundException("Academic year", id);
        if (await db.AcademicYears.AnyAsync(y => y.SchoolId == year.SchoolId && y.Name == dto.Name.Trim() && y.Id != id, ct))
            throw new ValidationException(nameof(dto.Name), "This school already has an academic year with this name.");
        await Apply(year, dto, false, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var year = await db.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == id, ct)
            ?? throw new NotFoundException("Academic year", id);
        if (await db.Classes.AnyAsync(c => c.AcademicYearId == id, ct))
            throw new ConflictException("Remove the classes of this academic year before deleting it.");
        foreach (var t in year.Terms) db.Terms.Remove(t);
        db.AcademicYears.Remove(year);
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(AcademicYearEditDto dto)
    {
        if (dto.EndDate <= dto.StartDate)
            throw new ValidationException(nameof(dto.EndDate), "The end date must be after the start date.");
        var terms = dto.Terms.OrderBy(t => t.StartDate).ToList();
        for (var i = 0; i < terms.Count; i++)
        {
            var t = terms[i];
            if (string.IsNullOrWhiteSpace(t.Name))
                throw new ValidationException("terms", "Every term needs a name.");
            if (t.EndDate <= t.StartDate || t.StartDate < dto.StartDate || t.EndDate > dto.EndDate)
                throw new ValidationException("terms", $"{t.Name} must fall inside the academic year and end after it starts.");
            if (i > 0 && t.StartDate <= terms[i - 1].EndDate)
                throw new ValidationException("terms", $"{t.Name} overlaps {terms[i - 1].Name}.");
        }
    }

    private async Task Apply(AcademicYear year, AcademicYearEditDto dto, bool forceCurrent, CancellationToken ct)
    {
        year.Name = dto.Name.Trim();
        year.StartDate = dto.StartDate;
        year.EndDate = dto.EndDate;

        if (dto.IsCurrent || forceCurrent)
        {
            // Only one current year per school.
            var others = await db.AcademicYears.Where(y => y.SchoolId == year.SchoolId && y.IsCurrent && y.Id != year.Id).ToListAsync(ct);
            others.ForEach(o => o.IsCurrent = false);
            year.IsCurrent = true;
        }
        else year.IsCurrent = false;

        // Terms are replaced as a set; matching ids are updated in place.
        var keep = dto.Terms.Where(t => t.Id is not null).Select(t => t.Id!.Value).ToHashSet();
        foreach (var removed in year.Terms.Where(t => !keep.Contains(t.Id)).ToList())
        {
            db.Terms.Remove(removed);
            year.Terms.Remove(removed);
        }
        var order = 0;
        foreach (var t in dto.Terms.OrderBy(t => t.StartDate))
        {
            var term = t.Id is { } tid ? year.Terms.FirstOrDefault(x => x.Id == tid) : null;
            if (term is null)
            {
                term = new Term { SchoolId = year.SchoolId, AcademicYearId = year.Id };
                year.Terms.Add(term);
            }
            term.Name = t.Name.Trim();
            term.StartDate = t.StartDate;
            term.EndDate = t.EndDate;
            term.SortOrder = order++;
        }
    }
}

public class CatalogService(IAppDbContext db, SchoolContext ctx, AccessScope scope)
{
    // ---------- Subjects ----------

    public async Task<List<SubjectDto>> ListSubjectsAsync(CancellationToken ct)
    {
        var classes = await ctx.ClassesInActiveYearAsync(ct);
        return await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new SubjectDto
        {
            Id = s.Id,
            Name = s.Name,
            Code = s.Code,
            Coefficient = s.Coefficient,
            Color = s.Color,
            Description = s.Description,
            IsActive = s.IsActive,
            ClassCount = db.TeachingAssignments.Count(a => a.SubjectId == s.Id && classes.Any(c => c.Id == a.ClassId))
        }).ToListAsync(ct);
    }

    public async Task<Guid> SaveSubjectAsync(Guid? id, SubjectEditDto dto, CancellationToken ct)
    {
        Subject subject;
        if (id is { } existing)
            subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == existing, ct) ?? throw new NotFoundException("Subject", existing);
        else
        {
            subject = new Subject { SchoolId = await ctx.RequireSchoolAsync(ct) };
            db.Subjects.Add(subject);
        }
        var code = dto.Code.Trim().ToUpperInvariant();
        if (await db.Subjects.AnyAsync(s => s.SchoolId == subject.SchoolId && s.Code == code && s.Id != subject.Id, ct))
            throw new ValidationException(nameof(dto.Code), "Another subject already uses this code.");

        subject.Name = dto.Name.Trim();
        subject.Code = code;
        subject.Coefficient = dto.Coefficient;
        subject.Color = dto.Color.ToLowerInvariant();
        subject.Description = dto.Description?.Trim();
        subject.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return subject.Id;
    }

    public async Task DeleteSubjectAsync(Guid id, CancellationToken ct)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Subject", id);
        if (await db.TeachingAssignments.AnyAsync(a => a.SubjectId == id, ct) || await db.TimetableSlots.AnyAsync(t => t.SubjectId == id, ct))
            throw new ConflictException("This subject is assigned to classes or the timetable. Deactivate it instead.");
        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Rooms ----------

    public async Task<List<RoomDto>> ListRoomsAsync(CancellationToken ct) =>
        await db.Rooms.AsNoTracking().OrderBy(r => r.Code).Select(r => new RoomDto
        {
            Id = r.Id, Name = r.Name, Code = r.Code, Building = r.Building, Capacity = r.Capacity, Type = r.Type
        }).ToListAsync(ct);

    public async Task<Guid> SaveRoomAsync(Guid? id, RoomEditDto dto, CancellationToken ct)
    {
        Room room;
        if (id is { } existing)
            room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == existing, ct) ?? throw new NotFoundException("Room", existing);
        else
        {
            room = new Room { SchoolId = await ctx.RequireSchoolAsync(ct) };
            db.Rooms.Add(room);
        }
        var code = dto.Code.Trim().ToUpperInvariant();
        if (await db.Rooms.AnyAsync(r => r.SchoolId == room.SchoolId && r.Code == code && r.Id != room.Id, ct))
            throw new ValidationException(nameof(dto.Code), "Another room already uses this code.");

        room.Name = dto.Name.Trim();
        room.Code = code;
        room.Building = dto.Building?.Trim();
        room.Capacity = dto.Capacity;
        room.Type = dto.Type?.Trim();
        await db.SaveChangesAsync(ct);
        return room.Id;
    }

    public async Task DeleteRoomAsync(Guid id, CancellationToken ct)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Room", id);
        if (await db.Classes.AnyAsync(c => c.RoomId == id, ct) || await db.TimetableSlots.AnyAsync(t => t.RoomId == id, ct))
            throw new ConflictException("This room is used by a class or the timetable.");
        db.Rooms.Remove(room);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>All dropdown data for the active school and year in one round trip.</summary>
    public async Task<SchoolLookupsDto> LookupsAsync(CancellationToken ct)
    {
        var classes = await ctx.ClassesInActiveYearAsync(ct);
        // Teachers and families only get the classes they belong to.
        var visible = await scope.VisibleClassIdsAsync(ct);
        if (visible is not null) classes = classes.Where(c => visible.Contains(c.Id));
        var yearId = await ctx.ResolveYearAsync(ct);
        var terms = yearId is null ? [] : await db.Terms.AsNoTracking().Where(t => t.AcademicYearId == yearId).OrderBy(t => t.StartDate).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new SchoolLookupsDto
        {
            AcademicYearId = yearId,
            Terms = terms.Select(t => new LookupItem(t.Id, t.Name)).ToList(),
            CurrentTermId = (terms.LastOrDefault(t => t.StartDate <= today) ?? terms.FirstOrDefault())?.Id,
            Years = await db.AcademicYears.AsNoTracking().OrderByDescending(y => y.StartDate)
                .Select(y => new LookupItem(y.Id, y.IsCurrent ? y.Name + " ✓" : y.Name)).ToListAsync(ct),
            Classes = await classes.AsNoTracking().OrderBy(c => c.Level).ThenBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct),
            Subjects = await db.Subjects.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new LookupItem(s.Id, s.Name)).ToListAsync(ct),
            Teachers = await db.Teachers.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.LastName).Select(t => new LookupItem(t.Id, t.FirstName + " " + t.LastName)).ToListAsync(ct),
            Rooms = await db.Rooms.AsNoTracking().OrderBy(r => r.Code).Select(r => new LookupItem(r.Id, r.Code + " · " + r.Name)).ToListAsync(ct),
            Guardians = await db.Guardians.AsNoTracking().Where(g => g.IsActive).OrderBy(g => g.LastName).Select(g => new LookupItem(g.Id, g.FirstName + " " + g.LastName)).ToListAsync(ct)
        };
    }
}
