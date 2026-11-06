using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Application.Services;

namespace SmartSchool.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<TenantService>();
        services.AddScoped<SchoolService>();
        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<PlanService>();
        services.AddScoped<PlanLimitService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AuditService>();
        services.AddScoped<TechnicalLogService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<StatusOptionService>();
        services.AddScoped<Services.Academics.SchoolContext>();
        services.AddScoped<Services.Academics.AccountLinker>();
        services.AddScoped<Services.Academics.AcademicYearService>();
        services.AddScoped<Services.Academics.CatalogService>();
        services.AddScoped<Services.Academics.PeopleService>();
        services.AddScoped<Services.Academics.ClassService>();
        services.AddScoped<Services.Academics.TimetableService>();
        services.AddScoped<Services.Academics.AccessScope>();
        services.AddScoped<Services.Academics.FileService>();
        services.AddScoped<Services.Academics.AttendanceService>();
        services.AddScoped<Services.Academics.HomeworkService>();
        services.AddScoped<Services.Academics.ExamService>();
        services.AddScoped<Services.Academics.ReportCardService>();
        return services;
    }
}
