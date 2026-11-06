using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

/// <summary>Tenant (organization) management. Only SuperAdmins reach these endpoints.</summary>
public class TenantService(IAppDbContext db, SubscriptionService subscriptions)
{
    public async Task<PagedResult<TenantDto>> ListAsync(PagedQuery query, CancellationToken ct)
    {
        var q = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(t => t.Name.Contains(s) || t.Code.Contains(s) || (t.ContactEmail != null && t.ContactEmail.Contains(s)));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "code" => q.OrderByDirection(t => t.Code, query.SortDesc),
            "status" => q.OrderByDirection(t => t.SubscriptionStatus, query.SortDesc),
            "created" => q.OrderByDirection(t => t.CreatedAtUtc, query.SortDesc),
            "end" => q.OrderByDirection(t => t.SubscriptionEndUtc, query.SortDesc),
            _ => q.OrderByDirection(t => t.Name, query.SortDesc)
        };

        return await q.Select(t => new TenantDto
        {
            Id = t.Id,
            Name = t.Name,
            Code = t.Code,
            ContactEmail = t.ContactEmail,
            Phone = t.Phone,
            Country = t.Country,
            IsActive = t.IsActive,
            SubscriptionStatus = t.SubscriptionStatus,
            SubscriptionEndUtc = t.SubscriptionEndUtc,
            PlanName = t.Plan != null ? t.Plan.Name : null,
            SchoolCount = t.Schools.Count(s => !s.IsDeleted),
            UserCount = t.Users.Count(u => !u.IsDeleted),
            CreatedAtUtc = t.CreatedAtUtc
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<TenantEditDto> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await Find(id, ct);
        return new TenantEditDto
        {
            Name = t.Name,
            Code = t.Code,
            ContactEmail = t.ContactEmail,
            Phone = t.Phone,
            Address = t.Address,
            Country = t.Country,
            DefaultLanguage = t.DefaultLanguage,
            IsActive = t.IsActive,
            PlanId = t.PlanId
        };
    }

    /// <summary>Creates the tenant and starts it on the chosen plan (a trial when the plan offers one).</summary>
    public async Task<Guid> CreateAsync(TenantEditDto dto, CancellationToken ct)
    {
        await EnsureUniqueCode(dto.Code, null, ct);
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == dto.PlanId && p.IsActive, ct)
            ?? throw new ValidationException(nameof(dto.PlanId), "Select the plan this tenant starts on.");

        var tenant = new Tenant();
        Apply(tenant, dto);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await subscriptions.CreateAsync(new SubscriptionCreateDto
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            BillingCycle = dto.BillingCycle,
            Status = plan.TrialDays > 0 ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
            CreateInvoice = true
        }, ct);
        return tenant.Id;
    }

    public async Task UpdateAsync(Guid id, TenantEditDto dto, CancellationToken ct)
    {
        var tenant = await Find(id, ct);
        await EnsureUniqueCode(dto.Code, id, ct);
        Apply(tenant, dto);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var tenant = await Find(id, ct);
        db.Tenants.Remove(tenant); // converted to a soft delete by the DbContext
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<LookupItem>> LookupAsync(CancellationToken ct) =>
        await db.Tenants.AsNoTracking().OrderBy(t => t.Name).Select(t => new LookupItem(t.Id, t.Name)).ToListAsync(ct);

    private async Task<Tenant> Find(Guid id, CancellationToken ct) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tenant", id);

    private async Task EnsureUniqueCode(string code, Guid? exceptId, CancellationToken ct)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Code == normalized && t.Id != exceptId, ct))
            throw new ValidationException(nameof(TenantEditDto.Code), "This code is already used by another tenant.");
    }

    private static void Apply(Tenant t, TenantEditDto dto)
    {
        t.Name = dto.Name.Trim();
        t.Code = dto.Code.Trim().ToLowerInvariant();
        t.ContactEmail = dto.ContactEmail?.Trim();
        t.Phone = dto.Phone?.Trim();
        t.Address = dto.Address?.Trim();
        t.Country = dto.Country?.Trim();
        t.DefaultLanguage = dto.DefaultLanguage;
        t.IsActive = dto.IsActive;
    }
}
