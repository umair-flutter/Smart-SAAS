using SmartSchool.Domain.Common;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

/// <summary>An application user. TenantId is null only for platform (SuperAdmin) users.</summary>
public class AppUser : BaseEntity
{
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginUtc { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public List<UserSchool> Schools { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>A role with a set of permissions. System roles have TenantId = null and cannot be deleted.</summary>
public class Role : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RoleType Type { get; set; }
    public bool IsSystem { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = string.Empty;
}

/// <summary>Grants a user access to a school (used for school-level roles).</summary>
public class UserSchool
{
    public Guid UserId { get; set; }
    public Guid SchoolId { get; set; }
    public School? School { get; set; }
}

/// <summary>A signed-in browser session, tracked for heartbeat, expiry and monitoring.</summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public bool IsActive(DateTime now) => RevokedAtUtc is null && ExpiresAtUtc > now;
}
