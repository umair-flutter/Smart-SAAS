using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;
using P = SmartSchool.Shared.Security.Permissions;

namespace SmartSchool.Application.Security;

/// <summary>The permission set each system role starts with. SuperAdmin implicitly has every permission.</summary>
public static class DefaultRolePermissions
{
    public static IReadOnlyList<string> For(RoleType type) => type switch
    {
        RoleType.SuperAdmin => Permissions.All.Select(p => p.Value).ToList(),
        RoleType.TenantAdmin => Permissions.All
            .Select(p => p.Value)
            .Where(p => !p.StartsWith("tenants.") && p != P.Monitoring.TechnicalLogs && p != P.Billing.Manage && p != P.Subscriptions.Manage)
            .ToList(),
        RoleType.SchoolAdmin =>
        [
            P.Dashboard.View, P.Schools.View,
            P.Users.View, P.Users.Create, P.Users.Edit,
            P.Roles.View,
            P.Students.View, P.Students.Create, P.Students.Edit, P.Students.Delete,
            P.Teachers.View, P.Teachers.Manage, P.Parents.View, P.Parents.Manage,
            P.Classes.View, P.Classes.Manage, P.AcademicYears.View, P.AcademicYears.Manage,
            P.Timetable.View, P.Timetable.Manage,
            P.Attendance.View, P.Attendance.Record, P.Attendance.Validate,
            P.Homework.View, P.Homework.Manage, P.Homework.Grade,
            P.Exams.View, P.Exams.Manage, P.Exams.EnterResults,
            P.ReportCards.View, P.ReportCards.Manage, P.ReportCards.Export,
            P.Notifications.View, P.Notifications.Send, P.Notifications.Broadcast,
            P.Monitoring.AuditLogs
        ],
        RoleType.Teacher =>
        [
            P.Dashboard.View, P.Students.View, P.Classes.View, P.Timetable.View,
            P.Attendance.View, P.Attendance.Record,
            P.Homework.View, P.Homework.Manage, P.Homework.Grade,
            P.Exams.View, P.Exams.EnterResults,
            P.ReportCards.View, P.ReportCards.Manage,
            P.Notifications.View, P.Notifications.Send
        ],
        RoleType.Parent =>
        [
            P.Dashboard.View, P.Timetable.View, P.Attendance.View, P.Homework.View,
            P.Exams.View, P.ReportCards.View, P.ReportCards.Export, P.Notifications.View
        ],
        RoleType.Student =>
        [
            P.Dashboard.View, P.Timetable.View, P.Attendance.View,
            P.Homework.View, P.Homework.Submit, P.Exams.View, P.ReportCards.View, P.Notifications.View
        ],
        _ => []
    };
}
