using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Api.Security;
using SmartSchool.Application.Services;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>The caller's inbox (every signed-in user).</summary>
    [HttpGet]
    public Task<PagedResult<NotificationDto>> Mine([FromQuery] PagedQuery query, [FromQuery] bool unread, CancellationToken ct) =>
        notifications.ListMineAsync(query, unread, ct);

    [HttpGet("unread-count")]
    public async Task<UnreadCountDto> Unread(CancellationToken ct) => new(await notifications.UnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<object> ReadAll(CancellationToken ct) => new { count = await notifications.MarkAllReadAsync(ct) };

    /// <summary>Sends a message. Targeted sends need notifications.send; role and platform announcements need notifications.broadcast.</summary>
    [HasPermission(Permissions.Notifications.Send)]
    [HttpPost("send")]
    public async Task<object> Send(SendNotificationDto dto, CancellationToken ct)
    {
        var (id, recipients) = await notifications.SendAsync(dto, ct);
        return new { id, recipients };
    }

    [HasPermission(Permissions.Notifications.Send)]
    [HttpGet("sent")]
    public Task<PagedResult<SentNotificationDto>> Sent([FromQuery] PagedQuery query, CancellationToken ct) => notifications.SentAsync(query, ct);
}

[ApiController]
[Authorize]
[Route("api/monitoring")]
public class MonitoringController(AuditService audit, TechnicalLogService logs, AnalyticsService analytics) : ControllerBase
{
    [HasPermission(Permissions.Monitoring.AuditLogs)]
    [HttpGet("audit")]
    public Task<PagedResult<AuditLogDto>> Audit([FromQuery] PagedQuery query, [FromQuery] string? entityType, [FromQuery] AuditAction? action,
        [FromQuery] string? entityId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        audit.ListAsync(query, entityType, action, entityId, from, to, ct);

    [HasPermission(Permissions.Monitoring.AuditLogs)]
    [HttpGet("audit/entity-types")]
    public Task<List<string>> EntityTypes(CancellationToken ct) => audit.EntityTypesAsync(ct);

    [HasPermission(Permissions.Monitoring.TechnicalLogs)]
    [HttpGet("logs")]
    public Task<PagedResult<TechnicalLogDto>> Logs([FromQuery] PagedQuery query, [FromQuery] string? level, CancellationToken ct) =>
        logs.ListAsync(query, level, ct);

    [HasPermission(Permissions.Monitoring.TechnicalLogs)]
    [HttpDelete("logs")]
    public async Task<object> PurgeLogs([FromQuery] int olderThanDays, CancellationToken ct) =>
        new { deleted = await logs.PurgeAsync(olderThanDays <= 0 ? 30 : olderThanDays, ct) };

    /// <summary>Records a page view from the client (any signed-in user).</summary>
    [HttpPost("page-view")]
    public async Task<IActionResult> PageView(PageViewDto dto, CancellationToken ct)
    {
        await analytics.RecordAsync(dto.Path, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Monitoring.Sessions)]
    [HttpGet("usage")]
    public Task<UsageAnalyticsDto> Usage([FromQuery] int days, CancellationToken ct) => analytics.GetAsync(days <= 0 ? 30 : days, ct);
}

[ApiController]
[Authorize]
[Route("api/status-options")]
public class StatusOptionsController(StatusOptionService statuses) : ControllerBase
{
    [HttpGet]
    public Task<List<StatusOptionDto>> List([FromQuery] StatusCategory? category, CancellationToken ct) => statuses.ListAsync(category, ct);

    [HasPermission(Permissions.Schools.Edit)]
    [HttpPost]
    public async Task<object> Create(StatusOptionDto dto, CancellationToken ct) => new { id = await statuses.SaveAsync(null, dto, ct) };

    [HasPermission(Permissions.Schools.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, StatusOptionDto dto, CancellationToken ct)
    {
        await statuses.SaveAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Schools.Edit)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await statuses.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/me")]
public class ProfileController(AuthService auth) : ControllerBase
{
    [HttpPut("profile")]
    public Task<CurrentUserDto> UpdateProfile(ProfileUpdateDto dto, CancellationToken ct) => auth.UpdateProfileAsync(dto, ct);

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(dto, ct);
        return NoContent();
    }
}
