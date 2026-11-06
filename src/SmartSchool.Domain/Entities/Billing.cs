using SmartSchool.Domain.Common;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

/// <summary>A commercial offer tenants subscribe to. Platform-level, not tenant-owned.</summary>
public class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Null means unlimited.</summary>
    public int? MaxSchools { get; set; }

    /// <summary>Null means unlimited.</summary>
    public int? MaxUsers { get; set; }

    public int TrialDays { get; set; } = 14;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public decimal PriceFor(BillingCycle cycle) => cycle == BillingCycle.Yearly ? PriceYearly : PriceMonthly;
}

/// <summary>One billing period of a tenant on a plan. A tenant keeps the full history.</summary>
public class Subscription : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; }
    public bool IsTrial { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    /// <summary>Price snapshot at the time of subscribing, so later plan price changes don't rewrite history.</summary>
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? Notes { get; set; }

    public bool Covers(DateTime nowUtc) => StartUtc <= nowUtc && EndUtc > nowUtc;
}

public class Invoice : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

    public string Number { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime IssueDateUtc { get; set; }
    public DateTime DueDateUtc { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public string? Notes { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];

    public decimal Balance => Total - AmountPaid;

    /// <summary>Recomputes totals from the lines. Amounts are rounded to cents.</summary>
    public void RecalculateTotals()
    {
        foreach (var line in Lines)
            line.Amount = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
        Subtotal = Lines.Sum(l => l.Amount);
        TaxAmount = Math.Round(Subtotal * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
        Total = Subtotal + TaxAmount;
    }

    /// <summary>Sets Paid / PartiallyPaid / Issued from the amount received.</summary>
    public void RefreshPaymentStatus()
    {
        if (Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled) return;
        Status = AmountPaid <= 0 ? InvoiceStatus.Issued
            : AmountPaid >= Total ? InvoiceStatus.Paid
            : InvoiceStatus.PartiallyPaid;
    }
}

public class InvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>A payment received against an invoice, recorded by a platform administrator.</summary>
public class Payment : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime PaidAtUtc { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}
