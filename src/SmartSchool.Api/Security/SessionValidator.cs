using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartSchool.Application.Common;
using SmartSchool.Infrastructure.Persistence;

namespace SmartSchool.Api.Security;

/// <summary>
/// Runs after the JWT signature is checked. Rejects tokens whose session was revoked or has expired,
/// and tokens of tenants whose subscription is no longer valid. Results are cached briefly.
/// </summary>
public sealed class SessionValidator(AppDbContext db, IMemoryCache cache, IOptions<SessionSettings> options)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static string CacheKey(Guid sessionId) => $"session:{sessionId}";

    public async Task<string?> ValidateAsync(Guid sessionId, Guid? tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(sessionId), out string? cached))
            return cached == "ok" ? null : cached;

        var error = await CheckAsync(sessionId, tenantId, ct);
        cache.Set(CacheKey(sessionId), error ?? "ok", CacheFor);
        return error;
    }

    private async Task<string?> CheckAsync(Guid sessionId, Guid? tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !session.IsActive(now))
            return "Your session has expired. Please sign in again.";

        if (tenantId is { } tid)
        {
            var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct);
            if (tenant is null || !tenant.HasAccess(now))
                return "Your organization's subscription is no longer active.";
        }

        // Any authenticated request counts as activity and slides the idle expiry forward.
        session.LastSeenUtc = now;
        session.ExpiresAtUtc = now.AddMinutes(options.Value.IdleTimeoutMinutes);
        await db.SaveChangesAsync(ct);
        return null;
    }
}
