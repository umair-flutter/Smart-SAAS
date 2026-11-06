using SmartSchool.Shared.Enums;

namespace SmartSchool.Domain.Entities;

/// <summary>
/// A message sent to one or more users. Delivery is tracked per recipient, so read state is per person.
/// TenantId is null for platform-wide announcements from a SuperAdmin.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Guid? SchoolId { get; set; }
    public Guid? SenderUserId { get; set; }
    public string? SenderName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    /// <summary>Relative client route opened by the notification's action button, e.g. "homework/{id}".</summary>
    public string? Link { get; set; }
    public string? ActionLabel { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public NotificationKind Kind { get; set; } = NotificationKind.Message;

    /// <summary>Human-readable audience, e.g. "Parents of Grade 5 A".</summary>
    public string? AudienceLabel { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<NotificationRecipient> Recipients { get; set; } = [];
}

public class NotificationRecipient
{
    public Guid NotificationId { get; set; }
    public Notification? Notification { get; set; }
    public Guid UserId { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

/// <summary>Who changed what and when. Written automatically for every entity change, plus sign-in events.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? TenantId { get; set; }
    public Guid? SchoolId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Summary { get; set; }

    /// <summary>JSON object of changed fields: { "Name": { "old": "A", "new": "B" } }.</summary>
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>Warnings and errors from the application log, kept in the database for the technical log viewer.</summary>
public class TechnicalLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? SourceContext { get; set; }
    public string? RequestPath { get; set; }
    public string? TraceId { get; set; }
}

/// <summary>
/// A tenant-defined status label (e.g. "Scholarship", "Repeating the year", "On leave") that can be
/// attached to students, teachers or parents and shown as a coloured badge.
/// </summary>
public class StatusOption : Common.BaseEntity, Common.ITenantOwned
{
    public Guid TenantId { get; set; }
    public StatusCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One page view in the client, used for usage analytics.</summary>
public class PageView
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? SchoolId { get; set; }
    public RoleType RoleType { get; set; }

    /// <summary>Normalised route, ids replaced by {id}: "students/{id}".</summary>
    public string Path { get; set; } = string.Empty;
}
