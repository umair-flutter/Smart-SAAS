namespace SmartSchool.Shared.Security;

/// <summary>Custom JWT claim names and HTTP header names used for context selection.</summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string RoleType = "role_type";
    public const string TenantId = "tenant_id";
    public const string SchoolIds = "school_ids";
    public const string SessionId = "sid";
    public const string Permission = "perm";
    public const string Language = "lang";
}

public static class AppHeaders
{
    /// <summary>Lets a SuperAdmin narrow the view to one tenant.</summary>
    public const string TenantId = "X-Tenant-Id";

    /// <summary>The active school context chosen in the top bar.</summary>
    public const string SchoolId = "X-School-Id";

    /// <summary>The academic year picked in the top bar; the school's current year when absent.</summary>
    public const string AcademicYearId = "X-Academic-Year-Id";
}
