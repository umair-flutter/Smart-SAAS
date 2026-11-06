using Microsoft.EntityFrameworkCore;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Common;

/// <summary>The authenticated caller and the active tenant/school context of the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? SessionId { get; }
    RoleType? RoleType { get; }
    bool IsSuperAdmin { get; }

    /// <summary>Effective tenant. For a SuperAdmin this is the tenant picked in the switcher, or null for "all tenants".</summary>
    Guid? TenantId { get; }

    /// <summary>Active school context, or null for "all schools the caller can see".</summary>
    Guid? SchoolId { get; }

    /// <summary>Schools assigned to a school-scoped user (SchoolAdmin, Teacher, Parent, Student).</summary>
    IReadOnlyCollection<Guid> AllowedSchoolIds { get; }

    /// <summary>Academic year picked in the top bar (unvalidated; services fall back to the school's current year).</summary>
    Guid? AcademicYearId { get; }

    string? UserName { get; }
    string? IpAddress { get; }

    bool HasPermission(string permission);
}

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<School> Schools { get; }
    DbSet<AppUser> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserSchool> UserSchools { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<Term> Terms { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Room> Rooms { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<Student> Students { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    DbSet<SchoolClass> Classes { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<TeachingAssignment> TeachingAssignments { get; }
    DbSet<TimetableSlot> TimetableSlots { get; }
    DbSet<StoredFile> Files { get; }
    DbSet<AbsenceReason> AbsenceReasons { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<Homework> Homework { get; }
    DbSet<HomeworkSubmission> HomeworkSubmissions { get; }
    DbSet<Exam> Exams { get; }
    DbSet<ExamResult> ExamResults { get; }
    DbSet<ReportCard> ReportCards { get; }
    DbSet<ReportCardComment> ReportCardComments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationRecipient> NotificationRecipients { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<TechnicalLog> TechnicalLogs { get; }
    DbSet<PageView> PageViews { get; }
    DbSet<StatusOption> StatusOptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user, Role role, IEnumerable<Guid> schoolIds, Guid sessionId);
}

/// <summary>Stores uploaded documents outside the database.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Guid tenantId, Stream content, string extension, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}

/// <summary>Renders a report card as PDF in the requested language (en, fr, ar).</summary>
public interface IReportCardPdf
{
    byte[] Render(Shared.Contracts.ReportCardDto card, string language);
}

public class SessionSettings
{
    public const string Section = "Session";

    /// <summary>A session expires after this many minutes without a request or heartbeat.</summary>
    public int IdleTimeoutMinutes { get; set; } = 60;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
