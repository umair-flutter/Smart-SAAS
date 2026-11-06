using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

public class PlanService(IAppDbContext db)
{
    public async Task<List<PlanDto>> ListAsync(bool activeOnly, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = db.Plans.AsNoTracking();
        if (activeOnly) q = q.Where(p => p.IsActive);
        return await q.OrderBy(p => p.SortOrder).ThenBy(p => p.PriceMonthly)
            .Select(p => new PlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Code = p.Code,
                Description = p.Description,
                PriceMonthly = p.PriceMonthly,
                PriceYearly = p.PriceYearly,
                Currency = p.Currency,
                MaxSchools = p.MaxSchools,
                MaxUsers = p.MaxUsers,
                TrialDays = p.TrialDays,
                IsActive = p.IsActive,
                SortOrder = p.SortOrder,
                ActiveSubscriptions = db.Subscriptions.IgnoreQueryFilters().Count(s => s.PlanId == p.Id && !s.IsDeleted
                    && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial) && s.StartUtc <= now && s.EndUtc > now)
            })
            .ToListAsync(ct);
    }

    public async Task<PlanEditDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await Find(id, ct);
        return new PlanEditDto
        {
            Name = p.Name, Code = p.Code, Description = p.Description,
            PriceMonthly = p.PriceMonthly, PriceYearly = p.PriceYearly, Currency = p.Currency,
            MaxSchools = p.MaxSchools, MaxUsers = p.MaxUsers, TrialDays = p.TrialDays,
            IsActive = p.IsActive, SortOrder = p.SortOrder
        };
    }

    public async Task<Guid> CreateAsync(PlanEditDto dto, CancellationToken ct)
    {
        await EnsureUniqueCode(dto.Code, null, ct);
        var plan = new Plan();
        Apply(plan, dto);
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);
        return plan.Id;
    }

    public async Task UpdateAsync(Guid id, PlanEditDto dto, CancellationToken ct)
    {
        var plan = await Find(id, ct);
        await EnsureUniqueCode(dto.Code, id, ct);
        Apply(plan, dto);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Plans that were ever used are kept for history; deactivate them instead.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var plan = await Find(id, ct);
        if (await db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.PlanId == id, ct))
            throw new ConflictException("This plan has subscriptions. Deactivate it instead of deleting it.");
        db.Plans.Remove(plan);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Plan> Find(Guid id, CancellationToken ct) =>
        await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Plan", id);

    private async Task EnsureUniqueCode(string code, Guid? exceptId, CancellationToken ct)
    {
        var c = code.Trim().ToLowerInvariant();
        if (await db.Plans.AnyAsync(p => p.Code == c && p.Id != exceptId, ct))
            throw new ValidationException(nameof(PlanEditDto.Code), "Another plan already uses this code.");
    }

    private static void Apply(Plan p, PlanEditDto dto)
    {
        p.Name = dto.Name.Trim();
        p.Code = dto.Code.Trim().ToLowerInvariant();
        p.Description = dto.Description?.Trim();
        p.PriceMonthly = Math.Round(dto.PriceMonthly, 2);
        p.PriceYearly = Math.Round(dto.PriceYearly, 2);
        p.Currency = dto.Currency.Trim().ToUpperInvariant();
        p.MaxSchools = dto.MaxSchools;
        p.MaxUsers = dto.MaxUsers;
        p.TrialDays = dto.TrialDays;
        p.IsActive = dto.IsActive;
        p.SortOrder = dto.SortOrder;
    }
}

/// <summary>Enforces the school and user limits of the tenant's current plan.</summary>
public class PlanLimitService(IAppDbContext db)
{
    public async Task EnsureCanAddSchoolAsync(Guid tenantId, CancellationToken ct)
    {
        var plan = await PlanOf(tenantId, ct);
        if (plan?.MaxSchools is not { } max) return;
        var used = await db.Schools.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId && !s.IsDeleted, ct);
        if (used >= max)
            throw new ValidationException("plan", $"The {plan.Name} plan allows {max} school(s). Upgrade the plan to add more.");
    }

    public async Task EnsureCanAddUserAsync(Guid tenantId, CancellationToken ct)
    {
        var plan = await PlanOf(tenantId, ct);
        if (plan?.MaxUsers is not { } max) return;
        var used = await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId && !u.IsDeleted, ct);
        if (used >= max)
            throw new ValidationException("plan", $"The {plan.Name} plan allows {max} user(s). Upgrade the plan to add more.");
    }

    private async Task<Plan?> PlanOf(Guid tenantId, CancellationToken ct) =>
        await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Plan).FirstOrDefaultAsync(ct);
}
