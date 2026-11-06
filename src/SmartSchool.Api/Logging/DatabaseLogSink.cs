using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using SmartSchool.Domain.Entities;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Logging;

/// <summary>
/// Writes warnings and errors to the TechnicalLogs table in batches, for the in-app technical log viewer.
/// Entity Framework's own messages are skipped so the sink never logs about itself.
/// </summary>
public sealed class DatabaseLogSink(IServiceProvider services) : IBatchedLogEventSink
{
    public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
    {
        var rows = batch
            .Where(e => !Source(e).StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(e => new TechnicalLog
            {
                TimestampUtc = e.Timestamp.UtcDateTime,
                Level = e.Level.ToString(),
                Message = Truncate(e.RenderMessage(), 4000)!,
                Exception = e.Exception?.ToString(),
                SourceContext = Truncate(Source(e), 300),
                RequestPath = Truncate(Prop(e, "RequestPath"), 400),
                TraceId = Truncate(Prop(e, "RequestId") ?? e.TraceId?.ToString(), 100)
            }).ToList();
        if (rows.Count == 0) return;

        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TechnicalLogs.AddRange(rows);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let logging failures break the application; the file log still has the entries.
            SelfLog.WriteLine("DatabaseLogSink failed: {0}", ex.Message);
        }
    }

    public Task OnEmptyBatchAsync() => Task.CompletedTask;

    private static string Source(LogEvent e) => Prop(e, "SourceContext") ?? string.Empty;

    private static string? Prop(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) && v is ScalarValue { Value: { } s } ? s.ToString() : null;

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
