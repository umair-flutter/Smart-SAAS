using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

/// <summary>Invoices and manually recorded payments. Tenant users see only their own (query filters).</summary>
public class InvoiceService(IAppDbContext db, ICurrentUser current, NotificationService notifications)
{
    public const int DefaultPaymentTermDays = 14;

    public async Task<PagedResult<InvoiceDto>> ListAsync(PagedQuery query, InvoiceStatus? status, bool overdueOnly, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = db.Invoices.AsNoTracking();
        if (status is not null) q = q.Where(i => i.Status == status);
        if (overdueOnly) q = q.Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDateUtc < now);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(i => i.Number.Contains(s) || i.Tenant!.Name.Contains(s));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "number" => q.OrderByDirection(i => i.Number, query.SortDesc),
            "tenant" => q.OrderByDirection(i => i.Tenant!.Name, query.SortDesc),
            "due" => q.OrderByDirection(i => i.DueDateUtc, query.SortDesc),
            "total" => q.OrderByDirection(i => i.Total, query.SortDesc),
            "status" => q.OrderByDirection(i => i.Status, query.SortDesc),
            _ => query.SortDesc ? q.OrderBy(i => i.IssueDateUtc).ThenBy(i => i.Number) : q.OrderByDescending(i => i.IssueDateUtc).ThenByDescending(i => i.Number)
        };

        return await q.Select(i => new InvoiceDto
        {
            Id = i.Id,
            Number = i.Number,
            TenantId = i.TenantId,
            TenantName = i.Tenant!.Name,
            Status = i.Status,
            IssueDateUtc = i.IssueDateUtc,
            DueDateUtc = i.DueDateUtc,
            Currency = i.Currency,
            Total = i.Total,
            AmountPaid = i.AmountPaid,
            IsOverdue = (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.DueDateUtc < now
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<InvoiceDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var i = await db.Invoices.AsNoTracking()
            .Include(x => x.Tenant)
            .Include(x => x.Subscription).ThenInclude(s => s!.Plan)
            .Include(x => x.Lines)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Invoice", id);

        return new InvoiceDetailDto
        {
            Id = i.Id,
            Number = i.Number,
            TenantId = i.TenantId,
            TenantName = i.Tenant!.Name,
            TenantEmail = i.Tenant.ContactEmail,
            TenantAddress = i.Tenant.Address,
            TenantCountry = i.Tenant.Country,
            SubscriptionId = i.SubscriptionId,
            PlanName = i.Subscription?.Plan?.Name,
            Status = i.Status,
            IssueDateUtc = i.IssueDateUtc,
            DueDateUtc = i.DueDateUtc,
            Currency = i.Currency,
            Subtotal = i.Subtotal,
            TaxRate = i.TaxRate,
            TaxAmount = i.TaxAmount,
            Total = i.Total,
            AmountPaid = i.AmountPaid,
            Notes = i.Notes,
            IsOverdue = i.Status.IsOpen() && i.DueDateUtc < now,
            Lines = i.Lines.OrderBy(l => l.SortOrder)
                .Select(l => new InvoiceLineDto { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Amount = l.Amount }).ToList(),
            Payments = i.Payments.Where(p => !p.IsDeleted).OrderBy(p => p.PaidAtUtc).Select(p => new PaymentDto
            {
                Id = p.Id, InvoiceId = i.Id, InvoiceNumber = i.Number, TenantName = i.Tenant.Name,
                Amount = p.Amount, Currency = p.Currency, PaidAtUtc = p.PaidAtUtc, Method = p.Method, Reference = p.Reference, Notes = p.Notes
            }).ToList()
        };
    }

    public async Task<Guid> CreateAsync(InvoiceCreateDto dto, CancellationToken ct)
    {
        var tenantId = dto.TenantId ?? current.TenantId
            ?? throw new ValidationException(nameof(dto.TenantId), "Select the tenant to invoice.");
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new ValidationException(nameof(dto.TenantId), "Select a valid tenant.");
        if (dto.Lines.Count == 0)
            throw new ValidationException(nameof(dto.Lines), "Add at least one line.");
        if (dto.DueDateUtc.Date < dto.IssueDateUtc.Date)
            throw new ValidationException(nameof(dto.DueDateUtc), "The due date cannot be before the issue date.");
        if (dto.SubscriptionId is { } sid && !await db.Subscriptions.AnyAsync(s => s.Id == sid && s.TenantId == tenantId, ct))
            throw new ValidationException(nameof(dto.SubscriptionId), "The subscription does not belong to this tenant.");

        var invoice = new Invoice
        {
            TenantId = tenantId,
            SubscriptionId = dto.SubscriptionId,
            Number = await NextNumberAsync(dto.IssueDateUtc, ct),
            Status = dto.Issue ? InvoiceStatus.Issued : InvoiceStatus.Draft,
            IssueDateUtc = DateTime.SpecifyKind(dto.IssueDateUtc.Date, DateTimeKind.Utc),
            DueDateUtc = DateTime.SpecifyKind(dto.DueDateUtc.Date, DateTimeKind.Utc),
            Currency = dto.Currency.Trim().ToUpperInvariant(),
            TaxRate = dto.TaxRate,
            Notes = dto.Notes?.Trim(),
            Lines = dto.Lines.Select((l, idx) => new InvoiceLine
            {
                Description = l.Description.Trim(), Quantity = l.Quantity, UnitPrice = l.UnitPrice, SortOrder = idx
            }).ToList()
        };
        invoice.RecalculateTotals();
        db.Invoices.Add(invoice);
        if (invoice.Status == InvoiceStatus.Issued) await AnnounceAsync(invoice, ct);
        await db.SaveChangesAsync(ct);
        return invoice.Id;
    }

    /// <summary>Adds (without saving) the invoice for a paid subscription period.</summary>
    public async Task<Invoice> AddSubscriptionInvoiceAsync(Subscription s, Plan plan, CancellationToken ct)
    {
        var issue = s.StartUtc > DateTime.UtcNow ? s.StartUtc.Date : DateTime.UtcNow.Date;
        var cycle = s.BillingCycle == BillingCycle.Yearly ? "yearly" : "monthly";
        var invoice = new Invoice
        {
            TenantId = s.TenantId,
            SubscriptionId = s.Id,
            Number = await NextNumberAsync(issue, ct),
            Status = InvoiceStatus.Issued,
            IssueDateUtc = DateTime.SpecifyKind(issue, DateTimeKind.Utc),
            DueDateUtc = DateTime.SpecifyKind(issue.AddDays(DefaultPaymentTermDays), DateTimeKind.Utc),
            Currency = s.Currency,
            Lines =
            [
                new InvoiceLine
                {
                    Description = $"{plan.Name} plan, {cycle} subscription ({s.StartUtc:yyyy-MM-dd} to {s.EndUtc:yyyy-MM-dd})",
                    Quantity = 1,
                    UnitPrice = s.Price
                }
            ]
        };
        invoice.RecalculateTotals();
        db.Invoices.Add(invoice);
        await AnnounceAsync(invoice, ct);
        return invoice;
    }

    public async Task IssueAsync(Guid id, CancellationToken ct)
    {
        var invoice = await Find(id, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ValidationException("status", "Only draft invoices can be issued.");
        invoice.Status = InvoiceStatus.Issued;
        await AnnounceAsync(invoice, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var invoice = await Find(id, ct);
        if (invoice.Status == InvoiceStatus.Cancelled) return;
        if (invoice.AmountPaid > 0)
            throw new ValidationException("status", "Remove the recorded payments before cancelling this invoice.");
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelledAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct)
    {
        var invoice = await Find(id, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ValidationException("status", "Only drafts can be deleted. Cancel issued invoices instead.");
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddPaymentAsync(Guid invoiceId, PaymentCreateDto dto, CancellationToken ct)
    {
        var invoice = await Find(invoiceId, ct);
        if (!invoice.Status.IsOpen())
            throw new ValidationException("status", "Payments can only be recorded on issued, unpaid invoices.");
        var amount = Math.Round(dto.Amount, 2);
        if (amount <= 0 || amount > invoice.Balance)
            throw new ValidationException(nameof(dto.Amount), $"Enter an amount between 0.01 and {invoice.Balance:0.00}.");
        if (dto.PaidAtUtc > DateTime.UtcNow.AddDays(1))
            throw new ValidationException(nameof(dto.PaidAtUtc), "The payment date cannot be in the future.");

        var payment = new Payment
        {
            TenantId = invoice.TenantId,
            InvoiceId = invoice.Id,
            Amount = amount,
            Currency = invoice.Currency,
            PaidAtUtc = DateTime.SpecifyKind(dto.PaidAtUtc, DateTimeKind.Utc),
            Method = dto.Method,
            Reference = dto.Reference?.Trim(),
            Notes = dto.Notes?.Trim()
        };
        db.Payments.Add(payment);
        invoice.AmountPaid += amount;
        invoice.RefreshPaymentStatus();
        await db.SaveChangesAsync(ct);
        return payment.Id;
    }

    public async Task DeletePaymentAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Payment", paymentId);
        db.Payments.Remove(payment);
        if (payment.Invoice is { } invoice)
        {
            invoice.AmountPaid = Math.Max(0, invoice.AmountPaid - payment.Amount);
            invoice.RefreshPaymentStatus();
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<PaymentDto>> ListPaymentsAsync(PagedQuery query, CancellationToken ct)
    {
        var q = db.Payments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(p => p.Invoice!.Number.Contains(s) || p.Invoice.Tenant!.Name.Contains(s) || (p.Reference != null && p.Reference.Contains(s)));
        }
        q = query.SortBy?.ToLowerInvariant() switch
        {
            "amount" => q.OrderByDirection(p => p.Amount, query.SortDesc),
            "tenant" => q.OrderByDirection(p => p.Invoice!.Tenant!.Name, query.SortDesc),
            _ => query.SortDesc ? q.OrderBy(p => p.PaidAtUtc) : q.OrderByDescending(p => p.PaidAtUtc)
        };
        return await q.Select(p => new PaymentDto
        {
            Id = p.Id,
            InvoiceId = p.InvoiceId,
            InvoiceNumber = p.Invoice!.Number,
            TenantName = p.Invoice.Tenant!.Name,
            Amount = p.Amount,
            Currency = p.Currency,
            PaidAtUtc = p.PaidAtUtc,
            Method = p.Method,
            Reference = p.Reference,
            Notes = p.Notes
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<BillingStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var open = db.Invoices.Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid);

        var activeSubs = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.StartUtc <= now && s.EndUtc > now)
            .Select(s => new { s.BillingCycle, s.Price, s.Currency })
            .ToListAsync(ct);

        return new BillingStatsDto
        {
            Currency = activeSubs.Select(s => s.Currency).FirstOrDefault() ?? "USD",
            Outstanding = await open.SumAsync(i => (decimal?)(i.Total - i.AmountPaid), ct) ?? 0,
            Overdue = await open.Where(i => i.DueDateUtc < now).SumAsync(i => (decimal?)(i.Total - i.AmountPaid), ct) ?? 0,
            CollectedThisMonth = await db.Payments.Where(p => p.PaidAtUtc >= monthStart).SumAsync(p => (decimal?)p.Amount, ct) ?? 0,
            MonthlyRecurringRevenue = Math.Round(activeSubs.Sum(s => s.BillingCycle == BillingCycle.Yearly ? s.Price / 12m : s.Price), 2),
            ExpiringIn30Days = await db.Tenants.CountAsync(t =>
                (t.SubscriptionStatus == SubscriptionStatus.Active || t.SubscriptionStatus == SubscriptionStatus.Trial)
                && t.SubscriptionEndUtc > now && t.SubscriptionEndUtc <= now.AddDays(30), ct),
            TenantsByStatus = (await db.Tenants.GroupBy(t => t.SubscriptionStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
                .ToDictionary(x => x.Key.ToString(), x => x.Count)
        };
    }

    /// <summary>Tells the tenant's administrators that a new invoice is waiting.</summary>
    private async Task AnnounceAsync(Invoice invoice, CancellationToken ct) =>
        notifications.Queue(await notifications.TenantAdminUserIdsAsync(invoice.TenantId, ct), $"New invoice {invoice.Number}",
            $"Amount due: {invoice.Total:0.00} {invoice.Currency} by {invoice.DueDateUtc:yyyy-MM-dd}.", $"invoices/{invoice.Id}", "View invoice",
            NotificationSeverity.Info, invoice.TenantId, null, "Organization administrators");

    private async Task<Invoice> Find(Guid id, CancellationToken ct) =>
        await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice", id);

    /// <summary>INV-YYYY-0001, sequential per year across all tenants.</summary>
    private async Task<string> NextNumberAsync(DateTime issueDate, CancellationToken ct)
    {
        var prefix = $"INV-{issueDate.Year}-";
        var last = await db.Invoices.IgnoreQueryFilters()
            .Where(i => i.Number.StartsWith(prefix))
            .OrderByDescending(i => i.Number)
            .Select(i => i.Number)
            .FirstOrDefaultAsync(ct);
        var pending = db.Invoices.Local.Where(i => i.Number.StartsWith(prefix)).Select(i => i.Number);
        var max = new[] { last }.Concat(pending)
            .Select(n => n is not null && int.TryParse(n[prefix.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:D4}";
    }
}
