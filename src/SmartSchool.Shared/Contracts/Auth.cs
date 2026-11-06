using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public CurrentUserDto User { get; set; } = new();
}

public class CurrentUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public RoleType RoleType { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string Language { get; set; } = "en";
    public List<string> Permissions { get; set; } = [];

    /// <summary>Schools this user can switch to in the top bar.</summary>
    public List<LookupItem> Schools { get; set; } = [];

    /// <summary>For SuperAdmin only: tenants available in the context switcher.</summary>
    public List<LookupItem> Tenants { get; set; } = [];

    public bool Has(string permission) => RoleType == RoleType.SuperAdmin || Permissions.Contains(permission);
}

/// <summary>Demo account shown on the login page in development only.</summary>
public record DemoAccount(string RoleName, string Initials, string Email, string Password);

public class SessionDto
{
    public Guid Id { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? TenantName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
