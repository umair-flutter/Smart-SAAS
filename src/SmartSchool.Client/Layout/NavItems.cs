using SmartSchool.Shared.Security;
using P = SmartSchool.Shared.Security.Permissions;

namespace SmartSchool.Client.Layout;

public sealed record NavItem(string Title, string Icon, string Href, string? Permission, bool Soon = false);

public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);

/// <summary>Sidebar structure. Items marked Soon open a placeholder until their module is built.</summary>
public static class NavItems
{
    public static readonly IReadOnlyList<NavSection> Sections =
    [
        new("Overview",
        [
            new("Dashboard", "ti-layout-dashboard", "", P.Dashboard.View)
        ]),
        new("Platform",
        [
            new("Tenants", "ti-building-community", "tenants", P.Tenants.View),
            new("Plans", "ti-package", "plans", P.Subscriptions.View),
            new("Subscriptions", "ti-calendar-dollar", "subscriptions", P.Subscriptions.View),
            new("Invoices", "ti-file-invoice", "invoices", P.Billing.View),
            new("Payments", "ti-cash", "payments", P.Billing.View),
            new("Users", "ti-users", "users", P.Users.View),
            new("Roles & permissions", "ti-shield-lock", "roles", P.Roles.View)
        ]),
        new("School",
        [
            new("Schools", "ti-school", "schools", P.Schools.View),
            new("Academic years", "ti-calendar", "academic-years", P.AcademicYears.View),
            new("Students", "ti-user-star", "students", P.Students.View),
            new("Parents & guardians", "ti-users-group", "parents", P.Parents.View),
            new("Teachers", "ti-chalkboard", "teachers", P.Teachers.View),
            new("Classes", "ti-users-group", "classes", P.Classes.View),
            new("Subjects", "ti-books", "subjects", P.Classes.View),
            new("Rooms", "ti-door", "rooms", P.Classes.View),
            new("Timetable", "ti-clock-hour-4", "timetable", P.Timetable.View)
        ]),
        new("Academics",
        [
            new("Attendance", "ti-checklist", "attendance", P.Attendance.View),
            new("Homework", "ti-notebook", "homework", P.Homework.View),
            new("Exams & results", "ti-certificate", "exams", P.Exams.View),
            new("Report cards", "ti-report", "report-cards", P.ReportCards.View)
        ]),
        new("Communication",
        [
            new("Notifications", "ti-bell", "notifications", null)
        ]),
        new("Monitoring",
        [
            new("Usage analytics", "ti-chart-bar", "monitoring/usage", P.Monitoring.Sessions),
            new("User sessions", "ti-device-desktop-analytics", "monitoring/sessions", P.Monitoring.Sessions),
            new("Audit trail", "ti-history", "monitoring/audit", P.Monitoring.AuditLogs),
            new("Technical logs", "ti-bug", "monitoring/logs", P.Monitoring.TechnicalLogs)
        ]),
        new("Settings",
        [
            new("Status options", "ti-tags", "settings/status-options", P.Schools.Edit),
            new("My profile", "ti-user-circle", "profile", null)
        ])
    ];
}
