using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using SmartSchool.Api.Security;
using SmartSchool.Application.Common;
using SmartSchool.Application.Services;
using SmartSchool.Infrastructure.Persistence;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, ICurrentUser current, IMemoryCache cache) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) =>
        await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);

    /// <summary>Quick-login accounts for the sign-in page. Returns an empty list unless Seed:ShowDemoAccounts is on.</summary>
    [AllowAnonymous]
    [HttpGet("demo-accounts")]
    public List<DemoAccount> DemoAccounts([FromServices] DbSeeder seeder) => seeder.GetDemoAccounts();

    [Authorize]
    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken ct) => auth.GetCurrentUserAsync(ct);

    [Authorize]
    [HttpPost("heartbeat")]
    public async Task<object> Heartbeat(CancellationToken ct) => new { expiresAtUtc = await auth.HeartbeatAsync(ct) };

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(ct);
        if (current.SessionId is { } sid) cache.Remove(SessionValidator.CacheKey(sid));
        return NoContent();
    }

    [HasPermission(Permissions.Monitoring.Sessions)]
    [HttpGet("sessions")]
    public Task<PagedResult<SessionDto>> Sessions([FromQuery] PagedQuery query, CancellationToken ct) => auth.GetSessionsAsync(query, ct);

    [HasPermission(Permissions.Monitoring.Sessions)]
    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await auth.RevokeSessionAsync(id, ct);
        cache.Remove(SessionValidator.CacheKey(id));
        return NoContent();
    }
}
