using System.ComponentModel.DataAnnotations;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Shared.Contracts;

// ---------- Notifications ----------

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Link { get; set; }
    public string? ActionLabel { get; set; }
    public NotificationSeverity Severity { get; set; }
    public NotificationKind Kind { get; set; }
    public string? SenderName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public class SentNotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public NotificationKind Kind { get; set; }
    public NotificationSeverity Severity { get; set; }
    public string? AudienceLabel { get; set; }
    public string? SenderName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int RecipientCount { get; set; }
    public int ReadCount { get; set; }
}

public class SendNotificationDto
{
    [Required, StringLength(200)] public string Title { get; set; } = string.Empty;
    [StringLength(4000)] public string? Body { get; set; }
    [StringLength(300)] public string? Link { get; set; }
    [StringLength(60)] public string? ActionLabel { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public AudienceType Audience { get; set; } = AudienceType.Roles;

    /// <summary>For Audience = Users.</summary>
    public List<Guid> UserIds { get; set; } = [];

    /// <summary>For Audience = Roles or Platform; empty means every role.</summary>
    public List<RoleType> Roles { get; set; } = [];

    /// <summary>For Audience = Class.</summary>
    public Guid? ClassId { get; set; }
    public bool IncludeStudents { get; set; } = true;
    public bool IncludeParents { get; set; } = true;
}

public record UnreadCountDto(int Count);

// ---------- Audit ----------

public class AuditLogDto
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
}

// ---------- Technical logs ----------

public class TechnicalLogDto
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

// ---------- Usage analytics ----------

public record PageViewDto(string Path);

public class UsageAnalyticsDto
{
    public int Days { get; set; }
    public int TotalViews { get; set; }
    public int ActiveUsers { get; set; }
    public int Sessions { get; set; }
    public List<DailyUsageDto> Daily { get; set; } = [];
    public List<PageUsageDto> TopPages { get; set; } = [];
    public List<RoleUsageDto> ByRole { get; set; } = [];
}

public record DailyUsageDto(DateOnly Date, int Views, int Users);
public record PageUsageDto(string Path, int Views, int Users);
public record RoleUsageDto(RoleType Role, int Views, int Users);

// ---------- Status options ----------

public class StatusOptionDto
{
    public Guid Id { get; set; }
    public StatusCategory Category { get; set; } = StatusCategory.Student;
    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Use a color like #64748b.")] public string Color { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int UsageCount { get; set; }
}

/// <summary>Badge shown next to a person.</summary>
public record StatusBadgeDto(Guid Id, string Name, string Color);

// ---------- Custom roles and profile ----------

public class RoleEditDto
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [StringLength(300)] public string? Description { get; set; }

    /// <summary>The access profile this role behaves as (scope of data). SuperAdmin is not allowed.</summary>
    public RoleType Type { get; set; } = RoleType.Teacher;

    public List<string> Permissions { get; set; } = [];
}

public class ChangePasswordDto
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}

public class ProfileUpdateDto
{
    [Required, StringLength(100)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string LastName { get; set; } = string.Empty;
    [StringLength(50)] public string? Phone { get; set; }
    [Required, RegularExpression("^(en|fr|ar)$")] public string Language { get; set; } = "en";
}
