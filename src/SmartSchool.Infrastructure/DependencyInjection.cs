using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Application.Common;
using SmartSchool.Infrastructure.Persistence;
using SmartSchool.Infrastructure.Security;

namespace SmartSchool.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection, sql =>
        {
            sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            sql.EnableRetryOnFailure(3);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtSettings>(config.GetSection(JwtSettings.Section));
        services.Configure<SessionSettings>(config.GetSection(SessionSettings.Section));
        services.Configure<SeedSettings>(config.GetSection(SeedSettings.Section));

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.Configure<Storage.StorageSettings>(config.GetSection(Storage.StorageSettings.Section));
        services.AddSingleton<IFileStorage, Storage.LocalFileStorage>();
        services.AddSingleton<IReportCardPdf, Pdf.ReportCardPdfRenderer>();
        services.AddScoped<DbSeeder>();
        return services;
    }
}
