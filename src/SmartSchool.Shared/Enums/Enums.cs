namespace SmartSchool.Shared.Enums;

/// <summary>The six access profiles of the platform, from widest to narrowest scope.</summary>
public enum RoleType
{
    SuperAdmin = 1,
    TenantAdmin = 2,
    SchoolAdmin = 3,
    Teacher = 4,
    Parent = 5,
    Student = 6
}

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    Cancelled = 4,
    Expired = 5
}

public static class RoleTypeExtensions
{
    /// <summary>True for roles that are limited to the schools explicitly assigned to the user.</summary>
    public static bool IsSchoolScoped(this RoleType type) => type >= RoleType.SchoolAdmin;

    public static string DisplayName(this RoleType type) => type switch
    {
        RoleType.SuperAdmin => "Super Admin",
        RoleType.TenantAdmin => "Tenant Administrator",
        RoleType.SchoolAdmin => "School Administrator",
        RoleType.Teacher => "Teacher",
        RoleType.Parent => "Parent / Guardian",
        RoleType.Student => "Student",
        _ => type.ToString()
    };
}
