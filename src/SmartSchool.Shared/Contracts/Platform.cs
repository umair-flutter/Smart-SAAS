using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

public class TenantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; }
    public SubscriptionStatus SubscriptionStatus { get; set; }
    public DateTime? SubscriptionEndUtc { get; set; }
    public string? PlanName { get; set; }
    public int SchoolCount { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class TenantEditDto
{
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(50), RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers and dashes only.")]
    public string Code { get; set; } = string.Empty;

    [EmailAddress, StringLength(200)]
    public string? ContactEmail { get; set; }

    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [Required] public string DefaultLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;

    /// <summary>Create only: the plan the new tenant starts on (as a trial when the plan has trial days).</summary>
    public Guid? PlanId { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
}

public class SchoolDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? PrincipalName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class SchoolEditDto
{
    /// <summary>Required for SuperAdmin; ignored for tenant users (their own tenant is used).</summary>
    public Guid? TenantId { get; set; }

    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [StringLength(300)] public string? Address { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(150)] public string? PrincipalName { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string? Phone { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public RoleType RoleType { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public List<Guid> SchoolIds { get; set; } = [];
    public List<string> SchoolNames { get; set; } = [];
    public bool IsActive { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public string PreferredLanguage { get; set; } = "en";
}

public class UserEditDto
{
    public Guid? TenantId { get; set; }
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string LastName { get; set; } = string.Empty;
    [StringLength(50)] public string? Phone { get; set; }
    [Required] public Guid RoleId { get; set; }
    public List<Guid> SchoolIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Required when creating a user; leave empty on edit to keep the current password.</summary>
    [StringLength(100, MinimumLength = 8)]
    public string? Password { get; set; }
}

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoleType Type { get; set; }
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = [];
    public int UserCount { get; set; }
}

public class RolePermissionsUpdateDto
{
    public List<string> Permissions { get; set; } = [];
}

public class DashboardStatsDto
{
    public int Tenants { get; set; }
    public int ActiveTenants { get; set; }
    public int Schools { get; set; }
    public int Users { get; set; }
    public int ActiveSessions { get; set; }
    public Dictionary<string, int> UsersByRole { get; set; } = [];
    public List<RecentLoginDto> RecentLogins { get; set; } = [];
    public BillingStatsDto? Billing { get; set; }
    public int Students { get; set; }
    public int Teachers { get; set; }
    public int Classes { get; set; }
}

public record RecentLoginDto(string FullName, string Email, string RoleName, DateTime? LastLoginUtc);
