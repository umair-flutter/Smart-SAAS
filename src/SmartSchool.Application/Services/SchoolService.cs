using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

/// <summary>
/// School management. The DbContext already limits rows to the caller's tenant;
/// this service further limits school-scoped roles to their assigned schools.
/// </summary>
public class SchoolService(IAppDbContext db, ICurrentUser current, PlanLimitService limits)
{
    private IQueryable<School> Visible()
    {
        var q = db.Schools.AsQueryable();
        if (current.RoleType?.IsSchoolScoped() == true)
        {
            var allowed = current.AllowedSchoolIds.ToList();
            q = q.Where(s => allowed.Contains(s.Id));
        }
        return q;
    }

    public async Task<PagedResult<SchoolDto>> ListAsync(PagedQuery query, CancellationToken ct)
    {
        var q = Visible().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(x => x.Name.Contains(s) || x.Code.Contains(s) || (x.City != null && x.City.Contains(s)));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "code" => q.OrderByDirection(x => x.Code, query.SortDesc),
            "tenant" => q.OrderByDirection(x => x.Tenant!.Name, query.SortDesc),
            "city" => q.OrderByDirection(x => x.City, query.SortDesc),
            "created" => q.OrderByDirection(x => x.CreatedAtUtc, query.SortDesc),
            _ => q.OrderByDirection(x => x.Name, query.SortDesc)
        };

        return await q.Select(x => new SchoolDto
        {
            Id = x.Id,
            TenantId = x.TenantId,
            TenantName = x.Tenant!.Name,
            Name = x.Name,
            Code = x.Code,
            Email = x.Email,
            Phone = x.Phone,
            City = x.City,
            PrincipalName = x.PrincipalName,
            IsActive = x.IsActive,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<SchoolEditDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await Find(id, ct);
        return new SchoolEditDto
        {
            TenantId = s.TenantId,
            Name = s.Name,
            Code = s.Code,
            Email = s.Email,
            Phone = s.Phone,
            Address = s.Address,
            City = s.City,
            Country = s.Country,
            PrincipalName = s.PrincipalName,
            IsActive = s.IsActive
        };
    }

    public async Task<Guid> CreateAsync(SchoolEditDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenant(dto.TenantId);
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new ValidationException(nameof(dto.TenantId), "Select a valid tenant.");
        await limits.EnsureCanAddSchoolAsync(tenantId, ct);
        await EnsureUniqueCode(tenantId, dto.Code, null, ct);

        var school = new School { TenantId = tenantId };
        Apply(school, dto);
        db.Schools.Add(school);
        await db.SaveChangesAsync(ct);
        return school.Id;
    }

    public async Task UpdateAsync(Guid id, SchoolEditDto dto, CancellationToken ct)
    {
        var school = await Find(id, ct);
        await EnsureUniqueCode(school.TenantId, dto.Code, id, ct);
        Apply(school, dto);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var school = await Find(id, ct);
        db.Schools.Remove(school);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<LookupItem>> LookupAsync(Guid? tenantId, CancellationToken ct)
    {
        var q = Visible().AsNoTracking();
        if (tenantId is not null) q = q.Where(s => s.TenantId == tenantId);
        return await q.OrderBy(s => s.Name).Select(s => new LookupItem(s.Id, s.Name)).ToListAsync(ct);
    }

    private Guid ResolveTenant(Guid? requested)
    {
        if (current.IsSuperAdmin)
            return requested ?? current.TenantId
                ?? throw new ValidationException(nameof(SchoolEditDto.TenantId), "Select the tenant this school belongs to.");
        return current.TenantId ?? throw new ForbiddenException();
    }

    private async Task<School> Find(Guid id, CancellationToken ct) =>
        await Visible().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("School", id);

    private async Task EnsureUniqueCode(Guid tenantId, string code, Guid? exceptId, CancellationToken ct)
    {
        var c = code.Trim().ToUpperInvariant();
        if (await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId && s.Code == c && s.Id != exceptId && !s.IsDeleted, ct))
            throw new ValidationException(nameof(SchoolEditDto.Code), "Another school in this tenant already uses this code.");
    }

    private static void Apply(School s, SchoolEditDto dto)
    {
        s.Name = dto.Name.Trim();
        s.Code = dto.Code.Trim().ToUpperInvariant();
        s.Email = dto.Email?.Trim();
        s.Phone = dto.Phone?.Trim();
        s.Address = dto.Address?.Trim();
        s.City = dto.City?.Trim();
        s.Country = dto.Country?.Trim();
        s.PrincipalName = dto.PrincipalName?.Trim();
        s.IsActive = dto.IsActive;
    }
}
