using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

public class PlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public string Currency { get; set; } = "USD";
    public int? MaxSchools { get; set; }
    public int? MaxUsers { get; set; }
    public int TrialDays { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int ActiveSubscriptions { get; set; }
}

public class PlanEditDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;

    [Required, StringLength(50), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and dashes only.")]
    public string Code { get; set; } = string.Empty;

    [StringLength(500)] public string? Description { get; set; }
    [Range(0, 1_000_000)] public decimal PriceMonthly { get; set; }
    [Range(0, 10_000_000)] public decimal PriceYearly { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "USD";
    [Range(1, 100_000)] public int? MaxSchools { get; set; }
    [Range(1, 10_000_000)] public int? MaxUsers { get; set; }
    [Range(0, 365)] public int TrialDays { get; set; } = 14;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public BillingCycle BillingCycle { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public bool AutoRenew { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? Notes { get; set; }
    public bool IsCurrent { get; set; }
    public int DaysRemaining { get; set; }
}

public class SubscriptionCreateDto
{
    [Required] public Guid? TenantId { get; set; }
    [Required] public Guid? PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    /// <summary>Trial or Active.</summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>Defaults to now.</summary>
    public DateTime? StartUtc { get; set; }

    public bool AutoRenew { get; set; } = true;

    /// <summary>Issue an invoice for the period (ignored for trials and free plans).</summary>
    public bool CreateInvoice { get; set; } = true;

    [StringLength(500)] public string? Notes { get; set; }
}

public class SubscriptionStatusChangeDto
{
    /// <summary>Active, Suspended or Cancelled.</summary>
    public SubscriptionStatus Status { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public class SubscriptionRenewDto
{
    public BillingCycle? BillingCycle { get; set; }
    public Guid? PlanId { get; set; }
    public bool CreateInvoice { get; set; } = true;
}

/// <summary>What a tenant sees about its own subscription.</summary>
public class SubscriptionOverviewDto
{
    public SubscriptionDto? Current { get; set; }
    public PlanDto? Plan { get; set; }
    public int SchoolsUsed { get; set; }
    public int UsersUsed { get; set; }
    public decimal OutstandingBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public List<SubscriptionDto> History { get; set; } = [];
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; }
    public DateTime IssueDateUtc { get; set; }
    public DateTime DueDateUtc { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance => Total - AmountPaid;
    public bool IsOverdue { get; set; }
}

public class InvoiceLineDto
{
    [Required, StringLength(300)] public string Description { get; set; } = string.Empty;
    [Range(0.01, 100_000)] public decimal Quantity { get; set; } = 1;
    [Range(0, 10_000_000)] public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public class InvoiceDetailDto : InvoiceDto
{
    public string? TenantEmail { get; set; }
    public string? TenantAddress { get; set; }
    public string? TenantCountry { get; set; }
    public Guid? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = [];
    public List<PaymentDto> Payments { get; set; } = [];
}

public class InvoiceCreateDto
{
    [Required] public Guid? TenantId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public DateTime IssueDateUtc { get; set; } = DateTime.UtcNow.Date;
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.Date.AddDays(14);
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "USD";
    [Range(0, 100)] public decimal TaxRate { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    [MinLength(1, ErrorMessage = "Add at least one line.")] public List<InvoiceLineDto> Lines { get; set; } = [new()];

    /// <summary>Issue immediately instead of saving as a draft.</summary>
    public bool Issue { get; set; } = true;
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime PaidAtUtc { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class PaymentCreateDto
{
    [Range(0.01, 10_000_000)] public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow.Date;
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    [StringLength(100)] public string? Reference { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class BillingStatsDto
{
    public string Currency { get; set; } = "USD";
    public decimal Outstanding { get; set; }
    public decimal Overdue { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public decimal MonthlyRecurringRevenue { get; set; }
    public int ExpiringIn30Days { get; set; }
    public Dictionary<string, int> TenantsByStatus { get; set; } = [];
}
