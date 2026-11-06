using SmartSchool.Domain.Common;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

/// <summary>A customer organization (school group). All business data is isolated per tenant.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;

    // The fields below mirror the tenant's current subscription so that every request can check
    // access without a join. SubscriptionService keeps them in sync.
    public Guid? PlanId { get; set; }
    public Plan? Plan { get; set; }
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trial;
    public DateTime? SubscriptionStartUtc { get; set; }
    public DateTime? SubscriptionEndUtc { get; set; }

    /// <summary>Tenant users may sign in only while the tenant is active and its subscription is valid.</summary>
    public bool HasAccess(DateTime nowUtc) =>
        IsActive && !IsDeleted
        && SubscriptionStatus is SubscriptionStatus.Trial or SubscriptionStatus.Active
        && (SubscriptionStartUtc is null || SubscriptionStartUtc <= nowUtc)
        && (SubscriptionEndUtc is null || SubscriptionEndUtc > nowUtc);

    public List<School> Schools { get; set; } = [];
    public List<AppUser> Users { get; set; } = [];
}
