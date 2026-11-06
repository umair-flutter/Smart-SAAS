using SmartSchool.Application.Services;

namespace SmartSchool.Api.Jobs;

/// <summary>Hourly: expires finished subscription periods and auto-renews paid ones.</summary>
public sealed class SubscriptionMaintenanceJob(IServiceScopeFactory scopes, ILogger<SubscriptionMaintenanceJob> logger, IConfiguration config)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(config.GetValue("Billing:MaintenanceIntervalMinutes", 60));
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var (expired, renewed) = await scope.ServiceProvider.GetRequiredService<SubscriptionService>().RunMaintenanceAsync(stoppingToken);
                // Housekeeping: keep technical logs and page views from growing forever.
                await scope.ServiceProvider.GetRequiredService<TechnicalLogService>().PurgeAsync(config.GetValue("Monitoring:TechnicalLogRetentionDays", 30), stoppingToken);
                await scope.ServiceProvider.GetRequiredService<AnalyticsService>().PurgeAsync(config.GetValue("Monitoring:PageViewRetentionDays", 365), stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Subscription maintenance: {Expired} expired, {Renewed} auto-renewed", expired, renewed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Subscription maintenance failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
