using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Application.Services.Academics;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Application.Services;

/// <summary>Inbox, sending, announcements and system notifications. Read state is stored per recipient.</summary>
public class NotificationService(IAppDbContext db, ICurrentUser current, AccessScope scope)
{
    // ---------- Inbox ----------

    public async Task<PagedResult<NotificationDto>> ListMineAsync(PagedQuery query, bool unreadOnly, CancellationToken ct)
    {
        var me = current.UserId ?? throw new UnauthorizedException("Not signed in.");
        var q = db.NotificationRecipients.AsNoTracking().Where(r => r.UserId == me);
        if (unreadOnly) q = q.Where(r => r.ReadAtUtc == null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(r => r.Notification!.Title.Contains(s) || (r.Notification.Body != null && r.Notification.Body.Contains(s)));
        }
        return await q.OrderByDescending(r => r.Notification!.CreatedAtUtc).Select(r => new NotificationDto
        {
            Id = r.NotificationId,
            Title = r.Notification!.Title,
            Body = r.Notification.Body,
            Link = r.Notification.Link,
            ActionLabel = r.Notification.ActionLabel,
            Severity = r.Notification.Severity,
            Kind = r.Notification.Kind,
            SenderName = r.Notification.SenderName,
            CreatedAtUtc = r.Notification.CreatedAtUtc,
            ReadAtUtc = r.ReadAtUtc
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var me = current.UserId ?? throw new UnauthorizedException("Not signed in.");
        return await db.NotificationRecipients.CountAsync(r => r.UserId == me && r.ReadAtUtc == null, ct);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct)
    {
        var me = current.UserId ?? throw new UnauthorizedException("Not signed in.");
        var r = await db.NotificationRecipients.FirstOrDefaultAsync(x => x.NotificationId == notificationId && x.UserId == me, ct)
            ?? throw new NotFoundException("Notification", notificationId);
        r.ReadAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var me = current.UserId ?? throw new UnauthorizedException("Not signed in.");
        var unread = await db.NotificationRecipients.Where(r => r.UserId == me && r.ReadAtUtc == null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        unread.ForEach(r => r.ReadAtUtc = now);
        await db.SaveChangesAsync(ct);
        return unread.Count;
    }

    // ---------- Sending ----------

    public async Task<(Guid Id, int Recipients)> SendAsync(SendNotificationDto dto, CancellationToken ct)
    {
        var (userIds, label, kind) = dto.Audience switch
        {
            AudienceType.Users => (await ResolveUsersAsync(dto.UserIds, ct), $"{dto.UserIds.Count} selected user(s)", NotificationKind.Message),
            AudienceType.Class => await ResolveClassAsync(dto, ct),
            AudienceType.Roles => await ResolveRolesAsync(dto.Roles, ct),
            AudienceType.Platform => await ResolvePlatformAsync(dto.Roles, ct),
            _ => throw new ValidationException(nameof(dto.Audience), "Choose who receives this message.")
        };
        if (userIds.Count == 0) throw new ValidationException("recipients", "Nobody matches this audience.");
        if (dto.Link is { } link && (link.Contains("//") || link.Contains(':')))
            throw new ValidationException(nameof(dto.Link), "Links must point to a page of this application, e.g. homework or invoices/….");

        var n = Build(dto.Title, dto.Body, dto.Link, dto.ActionLabel, dto.Severity, kind, label,
            dto.Audience == AudienceType.Platform ? null : current.TenantId, current.SchoolId, userIds);
        n.SenderUserId = current.UserId;
        n.SenderName = current.UserName;
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);
        return (n.Id, userIds.Count);
    }

    public async Task<PagedResult<SentNotificationDto>> SentAsync(PagedQuery query, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.Kind != NotificationKind.System);
        if (scope.IsAdmin && !current.IsSuperAdmin) q = q.Where(n => n.TenantId == current.TenantId);
        else if (!scope.IsAdmin) q = q.Where(n => n.SenderUserId == current.UserId);
        else if (current.TenantId is { } tid) q = q.Where(n => n.TenantId == tid || n.TenantId == null);

        return await q.OrderByDescending(n => n.CreatedAtUtc).Select(n => new SentNotificationDto
        {
            Id = n.Id, Title = n.Title, Kind = n.Kind, Severity = n.Severity, AudienceLabel = n.AudienceLabel,
            SenderName = n.SenderName, CreatedAtUtc = n.CreatedAtUtc,
            RecipientCount = n.Recipients.Count, ReadCount = n.Recipients.Count(r => r.ReadAtUtc != null)
        }).ToPagedResultAsync(query, ct);
    }

    private async Task<List<Guid>> ResolveUsersAsync(List<Guid> ids, CancellationToken ct)
    {
        if (!current.HasPermission(Permissions.Notifications.Send)) throw new ForbiddenException();
        // The user query filter limits this to the caller's tenant.
        var found = await db.Users.Where(u => ids.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        if (found.Count != ids.Distinct().Count()) throw new ValidationException("userIds", "Some recipients cannot be found.");
        return found;
    }

    private async Task<(List<Guid>, string, NotificationKind)> ResolveClassAsync(SendNotificationDto dto, CancellationToken ct)
    {
        if (!current.HasPermission(Permissions.Notifications.Send)) throw new ForbiddenException();
        var classId = dto.ClassId ?? throw new ValidationException(nameof(dto.ClassId), "Choose a class.");
        await scope.EnsureTeachesAsync(classId, null, ct);
        var name = await db.Classes.Where(c => c.Id == classId).Select(c => c.Name).FirstAsync(ct);
        var ids = await ClassFamilyUserIdsAsync(classId, dto.IncludeStudents, dto.IncludeParents, ct);
        var who = dto.IncludeStudents && dto.IncludeParents ? "Students and parents" : dto.IncludeStudents ? "Students" : "Parents";
        return (ids, $"{who} of {name}", NotificationKind.Message);
    }

    private async Task<(List<Guid>, string, NotificationKind)> ResolveRolesAsync(List<RoleType> roles, CancellationToken ct)
    {
        if (!current.HasPermission(Permissions.Notifications.Broadcast)) throw new ForbiddenException();
        if (current.TenantId is null) throw new ValidationException("tenant", "Choose a tenant in the top bar, or send a platform announcement.");
        var q = db.Users.Where(u => u.IsActive && u.TenantId == current.TenantId);
        if (roles.Count > 0) q = q.Where(u => roles.Contains(u.Role!.Type));

        // School-level senders (and anyone with a school selected) reach that school; tenant admins are always included.
        var schools = current.RoleType?.IsSchoolScoped() == true ? current.AllowedSchoolIds.ToList()
            : current.SchoolId is { } s ? [s] : null;
        if (schools is not null)
            q = q.Where(u => u.Role!.Type == RoleType.TenantAdmin || u.Schools.Any(x => schools.Contains(x.SchoolId)));

        var label = (roles.Count == 0 ? "Everyone" : string.Join(", ", roles.Select(r => r.DisplayName()))) +
            (schools is null ? " in the organization" : " of the selected school");
        return (await q.Select(u => u.Id).ToListAsync(ct), label, NotificationKind.Broadcast);
    }

    private async Task<(List<Guid>, string, NotificationKind)> ResolvePlatformAsync(List<RoleType> roles, CancellationToken ct)
    {
        if (!current.IsSuperAdmin) throw new ForbiddenException("Only a SuperAdmin can send platform announcements.");
        var q = db.Users.IgnoreQueryFilters().Where(u => !u.IsDeleted && u.IsActive);
        if (roles.Count > 0) q = q.Where(u => roles.Contains(u.Role!.Type));
        var label = roles.Count == 0 ? "All platform users" : $"{string.Join(", ", roles.Select(r => r.DisplayName()))} on all tenants";
        return (await q.Select(u => u.Id).ToListAsync(ct), label, NotificationKind.Broadcast);
    }

    // ---------- System notifications (used by other services) ----------

    /// <summary>Adds a system notification to the change tracker; it is saved with the caller's next SaveChanges.</summary>
    public void Queue(IEnumerable<Guid> userIds, string title, string? body, string? link, string? action,
        NotificationSeverity severity, Guid? tenantId, Guid? schoolId, string audience)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        db.Notifications.Add(Build(title, body, link, action, severity, NotificationKind.System, audience, tenantId, schoolId, ids));
    }

    /// <summary>Sign-in accounts of the active students of a class and/or their parents.</summary>
    public async Task<List<Guid>> ClassFamilyUserIdsAsync(Guid classId, bool students, bool parents, CancellationToken ct)
    {
        var studentIds = await db.Enrollments.Where(e => e.ClassId == classId && e.Status == EnrollmentStatus.Active).Select(e => e.StudentId).ToListAsync(ct);
        return await FamilyUserIdsAsync(studentIds, students, parents, ct);
    }

    public async Task<List<Guid>> FamilyUserIdsAsync(List<Guid> studentIds, bool students, bool parents, CancellationToken ct)
    {
        var ids = new List<Guid>();
        if (students)
            ids.AddRange(await db.Students.Where(s => studentIds.Contains(s.Id) && s.UserId != null).Select(s => s.UserId!.Value).ToListAsync(ct));
        if (parents)
            ids.AddRange(await db.StudentGuardians.Where(g => studentIds.Contains(g.StudentId) && g.Guardian!.UserId != null)
                .Select(g => g.Guardian!.UserId!.Value).ToListAsync(ct));
        return ids.Distinct().ToList();
    }

    public async Task<List<Guid>> TenantAdminUserIdsAsync(Guid tenantId, CancellationToken ct) =>
        await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted && u.IsActive && u.Role!.Type == RoleType.TenantAdmin)
            .Select(u => u.Id).ToListAsync(ct);

    private static Notification Build(string title, string? body, string? link, string? action, NotificationSeverity severity,
        NotificationKind kind, string? audience, Guid? tenantId, Guid? schoolId, List<Guid> userIds)
    {
        var n = new Notification
        {
            TenantId = tenantId,
            SchoolId = schoolId,
            Title = title.Trim(),
            Body = body?.Trim(),
            Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim().TrimStart('/'),
            ActionLabel = string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
            Severity = severity,
            Kind = kind,
            AudienceLabel = audience
        };
        n.Recipients.AddRange(userIds.Distinct().Select(id => new NotificationRecipient { NotificationId = n.Id, UserId = id }));
        return n;
    }
}
