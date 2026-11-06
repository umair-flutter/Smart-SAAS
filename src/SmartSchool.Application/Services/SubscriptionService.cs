using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

/// <summary>
/// Subscription lifecycle: start (trial or paid), renew, suspend, reactivate, cancel, and the
/// periodic expiry / auto-renewal run. After every change the tenant's cached access fields are re-synced.
/// </summary>
public class SubscriptionService(IAppDbContext db, ICurrentUser current, InvoiceService invoices)
{
    private static readonly SubscriptionStatus[] Running = [SubscriptionStatus.Trial, SubscriptionStatus.Active];

    public async Task<PagedResult<SubscriptionDto>> ListAsync(PagedQuery query, SubscriptionStatus? status, CancellationToken ct)
    {
        var q = db.Subscriptions.AsNoTracking();
        if (status is not null) q = q.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(s => s.Tenant!.Name.Contains(term) || s.Plan!.Name.Contains(term));
        }
        q = query.SortBy?.ToLowerInvariant() switch
        {
            "tenant" => q.OrderByDirection(s => s.Tenant!.Name, query.SortDesc),
            "plan" => q.OrderByDirection(s => s.Plan!.Name, query.SortDesc),
            "end" => q.OrderByDirection(s => s.EndUtc, query.SortDesc),
            "status" => q.OrderByDirection(s => s.Status, query.SortDesc),
            _ => query.SortDesc ? q.OrderBy(s => s.StartUtc) : q.OrderByDescending(s => s.StartUtc)
        };

        var page = await Project(q).ToPagedResultAsync(query, ct);
        page.Items.ForEach(Finish);
        return page;
    }

    /// <summary>The caller's own tenant subscription, plan limits, usage and balance.</summary>
    public async Task<SubscriptionOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var tenantId = current.TenantId ?? throw new ValidationException("tenant", "Select a tenant first.");
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("Tenant", tenantId);

        var history = await Project(db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartUtc)).ToListAsync(ct);
        history.ForEach(Finish);

        var plan = tenant.Plan;
        return new SubscriptionOverviewDto
        {
            Current = history.FirstOrDefault(h => h.IsCurrent) ?? history.FirstOrDefault(),
            Plan = plan is null ? null : new PlanDto
            {
                Id = plan.Id, Name = plan.Name, Code = plan.Code, Description = plan.Description,
                PriceMonthly = plan.PriceMonthly, PriceYearly = plan.PriceYearly, Currency = plan.Currency,
                MaxSchools = plan.MaxSchools, MaxUsers = plan.MaxUsers, TrialDays = plan.TrialDays, IsActive = plan.IsActive
            },
            SchoolsUsed = await db.Schools.CountAsync(s => s.TenantId == tenantId, ct),
            UsersUsed = await db.Users.CountAsync(u => u.TenantId == tenantId, ct),
            OutstandingBalance = await db.Invoices
                .Where(i => i.TenantId == tenantId && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid))
                .SumAsync(i => (decimal?)(i.Total - i.AmountPaid), ct) ?? 0,
            Currency = plan?.Currency ?? "USD",
            History = history
        };
    }

    public async Task<Guid> CreateAsync(SubscriptionCreateDto dto, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == dto.TenantId, ct)
            ?? throw new ValidationException(nameof(dto.TenantId), "Select a valid tenant.");
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == dto.PlanId && p.IsActive, ct)
            ?? throw new ValidationException(nameof(dto.PlanId), "Select an active plan.");
        if (dto.Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
            throw new ValidationException(nameof(dto.Status), "A new subscription starts as Trial or Active.");
        if (dto.Status == SubscriptionStatus.Trial && plan.TrialDays <= 0)
            throw new ValidationException(nameof(dto.Status), $"The {plan.Name} plan has no trial period.");
        // Backdating would silently close or cancel periods the tenant already paid for.
        if (dto.StartUtc is { } requestedStart && requestedStart < DateTime.UtcNow.Date.AddDays(-1))
            throw new ValidationException(nameof(dto.StartUtc), "A new subscription cannot start in the past.");

        var sub = await StartAsync(tenant, plan, dto.BillingCycle, dto.Status, dto.StartUtc ?? DateTime.UtcNow,
            dto.AutoRenew, dto.CreateInvoice, dto.Notes?.Trim(), ct);
        await db.SaveChangesAsync(ct);
        await SyncTenantAsync(tenant.Id, ct);
        await db.SaveChangesAsync(ct);
        return sub.Id;
    }

    public async Task<Guid> RenewAsync(Guid id, SubscriptionRenewDto dto, CancellationToken ct)
    {
        var sub = await db.Subscriptions.Include(s => s.Tenant).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Subscription", id);
        if (sub.Status == SubscriptionStatus.Suspended)
            throw new ValidationException("status", "Reactivate the suspended subscription before renewing it.");

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == (dto.PlanId ?? sub.PlanId) && p.IsActive, ct)
            ?? throw new ValidationException(nameof(dto.PlanId), "Select an active plan.");

        var now = DateTime.UtcNow;
        // A paid period still running is extended from its end date; trials and lapsed periods restart now.
        var start = sub.Status == SubscriptionStatus.Active && sub.EndUtc > now ? sub.EndUtc : now;
        var renewed = await StartAsync(sub.Tenant!, plan, dto.BillingCycle ?? sub.BillingCycle, SubscriptionStatus.Active, start,
            sub.AutoRenew || sub.Status == SubscriptionStatus.Trial, dto.CreateInvoice, "Renewal", ct);
        await db.SaveChangesAsync(ct);
        await SyncTenantAsync(sub.TenantId, ct);
        await db.SaveChangesAsync(ct);
        return renewed.Id;
    }

    public async Task ChangeStatusAsync(Guid id, SubscriptionStatusChangeDto dto, CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Subscription", id);
        var now = DateTime.UtcNow;
        var note = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

        switch (dto.Status)
        {
            case SubscriptionStatus.Suspended when sub.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial:
                sub.Status = SubscriptionStatus.Suspended;
                break;
            case SubscriptionStatus.Active when sub.Status == SubscriptionStatus.Suspended:
                if (sub.EndUtc <= now)
                    throw new ValidationException("status", "This period has already ended. Renew the subscription instead.");
                sub.Status = sub.IsTrial ? SubscriptionStatus.Trial : SubscriptionStatus.Active;
                break;
            case SubscriptionStatus.Cancelled when sub.Status != SubscriptionStatus.Cancelled:
                sub.Status = SubscriptionStatus.Cancelled;
                sub.CancelledAtUtc = now;
                sub.AutoRenew = false;
                break;
            default:
                throw new ValidationException("status", $"A {sub.Status} subscription cannot be changed to {dto.Status}.");
        }
        if (note is not null) sub.Notes = note;

        await db.SaveChangesAsync(ct);
        await SyncTenantAsync(sub.TenantId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAutoRenewAsync(Guid id, bool autoRenew, CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Subscription", id);
        sub.AutoRenew = autoRenew;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds a new subscription period (unsaved). Overlapping running periods of the tenant are closed
    /// at the new start date, so there is only ever one running period at a time.
    /// </summary>
    internal async Task<Subscription> StartAsync(Tenant tenant, Plan plan, BillingCycle cycle, SubscriptionStatus status,
        DateTime start, bool autoRenew, bool createInvoice, string? notes, CancellationToken ct)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        var trial = status == SubscriptionStatus.Trial;
        var end = trial ? start.AddDays(plan.TrialDays)
            : cycle == BillingCycle.Yearly ? start.AddYears(1) : start.AddMonths(1);

        var overlapping = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id && !s.IsDeleted && s.EndUtc > start
                && (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Suspended))
            .ToListAsync(ct);
        foreach (var old in overlapping)
        {
            if (old.StartUtc >= start)
            {
                old.Status = SubscriptionStatus.Cancelled;
                old.CancelledAtUtc = DateTime.UtcNow;
            }
            else
            {
                old.EndUtc = start;
                if (start <= DateTime.UtcNow) old.Status = SubscriptionStatus.Expired;
            }
            old.AutoRenew = false;
        }

        var sub = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = status,
            IsTrial = trial,
            BillingCycle = cycle,
            StartUtc = start,
            EndUtc = end,
            Price = trial ? 0 : plan.PriceFor(cycle),
            Currency = plan.Currency,
            AutoRenew = !trial && autoRenew,
            Notes = notes
        };
        db.Subscriptions.Add(sub);

        if (createInvoice && sub.Price > 0)
            await invoices.AddSubscriptionInvoiceAsync(sub, plan, ct);
        return sub;
    }

    /// <summary>Copies the current subscription onto the tenant row used by sign-in and request checks.</summary>
    public async Task SyncTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId, ct);
        var subs = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted).OrderBy(s => s.StartUtc).ToListAsync(ct);
        if (subs.Count == 0) return;

        var currentSub = subs.LastOrDefault(s => s.Covers(now) && s.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.Suspended)
            ?? subs.Where(s => s.StartUtc > now && Running.Contains(s.Status)).FirstOrDefault()
            ?? subs.OrderBy(s => s.EndUtc).Last();

        var status = currentSub.Status;
        if (Running.Contains(status) && currentSub.EndUtc <= now) status = SubscriptionStatus.Expired;

        // Follow back-to-back renewals so the tenant's end date is the end of the whole paid chain.
        var end = currentSub.EndUtc;
        if (Running.Contains(status))
            foreach (var s in subs.Where(s => s.StartUtc >= currentSub.StartUtc && s.Id != currentSub.Id && Running.Contains(s.Status)))
                if (s.StartUtc <= end) end = s.EndUtc > end ? s.EndUtc : end;

        tenant.PlanId = currentSub.PlanId;
        tenant.SubscriptionStatus = status;
        tenant.SubscriptionStartUtc = currentSub.StartUtc;
        tenant.SubscriptionEndUtc = end;
    }

    /// <summary>
    /// Expires finished periods and auto-renews paid ones (issuing their invoice). Called by the background job.
    /// Runs without a user, so it reads across all tenants explicitly.
    /// </summary>
    public async Task<(int Expired, int Renewed)> RunMaintenanceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Plan).Include(s => s.Tenant)
            .Where(s => !s.IsDeleted && (s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Active) && s.EndUtc <= now)
            .ToListAsync(ct);
        if (due.Count == 0) return (0, 0);

        var renewed = 0;
        foreach (var s in due)
        {
            var wasPaid = s.Status == SubscriptionStatus.Active;
            s.Status = SubscriptionStatus.Expired;

            var continued = await db.Subscriptions.IgnoreQueryFilters().AnyAsync(x => x.TenantId == s.TenantId && !x.IsDeleted && x.Id != s.Id
                && x.StartUtc >= s.EndUtc.AddMinutes(-1) && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trial), ct);

            if (!continued && wasPaid && s.AutoRenew && s.Plan is { IsActive: true } plan && s.Tenant is { IsActive: true, IsDeleted: false } tenant)
            {
                await StartAsync(tenant, plan, s.BillingCycle, SubscriptionStatus.Active, s.EndUtc, true, true, "Automatic renewal", ct);
                renewed++;
            }
        }
        await db.SaveChangesAsync(ct);

        foreach (var tenantId in due.Select(s => s.TenantId).Distinct())
            await SyncTenantAsync(tenantId, ct);
        await db.SaveChangesAsync(ct);
        return (due.Count, renewed);
    }

    private static IQueryable<SubscriptionDto> Project(IQueryable<Subscription> q) => q.Select(s => new SubscriptionDto
    {
        Id = s.Id,
        TenantId = s.TenantId,
        TenantName = s.Tenant!.Name,
        PlanId = s.PlanId,
        PlanName = s.Plan!.Name,
        Status = s.Status,
        BillingCycle = s.BillingCycle,
        StartUtc = s.StartUtc,
        EndUtc = s.EndUtc,
        Price = s.Price,
        Currency = s.Currency,
        AutoRenew = s.AutoRenew,
        CancelledAtUtc = s.CancelledAtUtc,
        Notes = s.Notes
    });

    private static void Finish(SubscriptionDto d)
    {
        var now = DateTime.UtcNow;
        d.IsCurrent = d.StartUtc <= now && d.EndUtc > now
            && d.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.Suspended;
        d.DaysRemaining = d.EndUtc > now ? (int)Math.Ceiling((d.EndUtc - now).TotalDays) : 0;
    }
}
