using System.Reflection;

namespace SmartSchool.Shared.Security;

/// <summary>
/// Every module-level permission in the platform. The API enforces these on each endpoint;
/// the client only uses them to hide menu items and buttons.
/// </summary>
public static class Permissions
{
    public static class Tenants
    {
        public const string View = "tenants.view";
        public const string Create = "tenants.create";
        public const string Edit = "tenants.edit";
        public const string Delete = "tenants.delete";
    }

    public static class Schools
    {
        public const string View = "schools.view";
        public const string Create = "schools.create";
        public const string Edit = "schools.edit";
        public const string Delete = "schools.delete";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Create = "users.create";
        public const string Edit = "users.edit";
        public const string Delete = "users.delete";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Manage = "roles.manage";
    }

    public static class Subscriptions
    {
        public const string View = "subscriptions.view";
        public const string Manage = "subscriptions.manage";
    }

    public static class Billing
    {
        public const string View = "billing.view";
        public const string Manage = "billing.manage";
    }

    public static class Students
    {
        public const string View = "students.view";
        public const string Create = "students.create";
        public const string Edit = "students.edit";
        public const string Delete = "students.delete";
    }

    public static class Teachers
    {
        public const string View = "teachers.view";
        public const string Manage = "teachers.manage";
    }

    public static class Parents
    {
        public const string View = "parents.view";
        public const string Manage = "parents.manage";
    }

    public static class Classes
    {
        public const string View = "classes.view";
        public const string Manage = "classes.manage";
    }

    public static class AcademicYears
    {
        public const string View = "academicyears.view";
        public const string Manage = "academicyears.manage";
    }

    public static class Timetable
    {
        public const string View = "timetable.view";
        public const string Manage = "timetable.manage";
    }

    public static class Attendance
    {
        public const string View = "attendance.view";
        public const string Record = "attendance.record";
        public const string Validate = "attendance.validate";
    }

    public static class Homework
    {
        public const string View = "homework.view";
        public const string Manage = "homework.manage";
        public const string Submit = "homework.submit";
        public const string Grade = "homework.grade";
    }

    public static class Exams
    {
        public const string View = "exams.view";
        public const string Manage = "exams.manage";
        public const string EnterResults = "exams.results";
    }

    public static class ReportCards
    {
        public const string View = "reportcards.view";
        public const string Manage = "reportcards.manage";
        public const string Export = "reportcards.export";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
        public const string Send = "notifications.send";
        public const string Broadcast = "notifications.broadcast";
    }

    public static class Monitoring
    {
        public const string AuditLogs = "monitoring.audit";
        public const string TechnicalLogs = "monitoring.technical";
        public const string Sessions = "monitoring.sessions";
    }

    public static class Dashboard
    {
        public const string View = "dashboard.view";
    }

    private static readonly Lazy<IReadOnlyList<PermissionInfo>> _all = new(() =>
        typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(module => module
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => new PermissionInfo(module.Name, f.Name, (string)f.GetRawConstantValue()!)))
            .ToList());

    /// <summary>All permissions, grouped by module name.</summary>
    public static IReadOnlyList<PermissionInfo> All => _all.Value;
}

public sealed record PermissionInfo(string Module, string Action, string Value);
