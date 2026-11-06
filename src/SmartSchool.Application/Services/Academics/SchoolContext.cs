using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services.Academics;

/// <summary>Resolves the active school and academic year for school-level services.</summary>
public class SchoolContext(IAppDbContext db, ICurrentUser current)
{
    /// <summary>The school picked in the top bar. Creating school data requires one.</summary>
    public async Task<Guid> RequireSchoolAsync(CancellationToken ct)
    {
        if (current.SchoolId is { } id && id != Guid.Empty && await db.Schools.AnyAsync(s => s.Id == id, ct))
            return id;
        throw new ValidationException("school", "Select a school in the top bar first.");
    }

    /// <summary>
    /// The academic year picked in the top bar if it is visible to the caller; otherwise the active school's
    /// current year; null when no school is selected (callers then use every school's current year).
    /// </summary>
    public async Task<Guid?> ResolveYearAsync(CancellationToken ct)
    {
        if (current.AcademicYearId is { } requested && await db.AcademicYears.AnyAsync(y => y.Id == requested, ct))
            return requested;
        if (current.SchoolId is null) return null;
        return await db.AcademicYears.Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<Guid> RequireYearAsync(CancellationToken ct) =>
        await ResolveYearAsync(ct) ?? throw new ValidationException("academicYear", "Create an academic year for this school and mark it as current.");

    public async Task<IQueryable<SchoolClass>> ClassesInActiveYearAsync(CancellationToken ct)
    {
        var year = await ResolveYearAsync(ct);
        return year is { } y ? db.Classes.Where(c => c.AcademicYearId == y) : db.Classes.Where(c => c.AcademicYear!.IsCurrent);
    }
}

/// <summary>Creates sign-in accounts for teachers, students and parents through the normal user rules.</summary>
public class AccountLinker(IAppDbContext db, ICurrentUser current, UserService users)
{
    public async Task<Guid?> CreateLoginAsync(RoleType type, PersonEditDto dto, Guid schoolId, CancellationToken ct)
    {
        if (!dto.CreateLogin) return null;
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ValidationException(nameof(dto.Email), "An email address is required to create a login.");
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new ValidationException(nameof(dto.Password), "A password is required to create a login.");

        var role = await db.Roles.FirstOrDefaultAsync(r => r.IsSystem && r.Type == type, ct)
            ?? throw new ValidationException("role", $"The {type} role is missing.");

        return await users.CreateAsync(new UserEditDto
        {
            TenantId = current.TenantId,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            RoleId = role.Id,
            SchoolIds = [schoolId],
            Password = dto.Password,
            IsActive = dto.IsActive
        }, ct);
    }
}
