using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context. Global query filters enforce soft delete, tenant isolation and
/// the active school context on every query, so services cannot leak another tenant's data by accident.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    // These members are referenced inside the query filters; EF Core re-evaluates them per query.
    private Guid? CurrentTenantId => currentUser.TenantId;
    private Guid? CurrentSchoolId => currentUser.SchoolId;
    private bool IsSuperAdmin => currentUser.IsSuperAdmin;
    private bool SeeAllTenants =>currentUser.IsSuperAdmin && currentUser.TenantId == null;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<School> Schools => Set<School>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserSchool> UserSchools => Set<UserSchool>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TeachingAssignment> TeachingAssignments => Set<TeachingAssignment>();
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<AbsenceReason> AbsenceReasons => Set<AbsenceReason>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Homework> Homework => Set<Homework>();
    public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<ReportCard> ReportCards => Set<ReportCard>();
    public DbSet<ReportCardComment> ReportCardComments => Set<ReportCardComment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TechnicalLog> TechnicalLogs => Set<TechnicalLog>();
    public DbSet<PageView> PageViews => Set<PageView>();
    public DbSet<StatusOption> StatusOptions => Set<StatusOption>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Tenants: soft delete; a SuperAdmin always sees every tenant, a tenant user only their own row.
        b.Entity<Tenant>().HasQueryFilter(t => !t.IsDeleted && (IsSuperAdmin || t.Id == CurrentTenantId));

        // Users and roles have a nullable TenantId (platform users / system roles).
        b.Entity<AppUser>().HasQueryFilter(u => !u.IsDeleted && (SeeAllTenants || u.TenantId == CurrentTenantId));
        b.Entity<Role>().HasQueryFilter(r => !r.IsDeleted && (SeeAllTenants || r.TenantId == null || r.TenantId == CurrentTenantId));

        // Every other BaseEntity gets a filter matching the ownership interfaces it implements.
        foreach (var type in b.Model.GetEntityTypes().Select(e => e.ClrType)
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t) && t != typeof(Tenant) && t != typeof(AppUser) && t != typeof(Role)))
        {
            var method = typeof(ISchoolOwned).IsAssignableFrom(type) ? nameof(SchoolFilter)
                : typeof(ITenantOwned).IsAssignableFrom(type) ? nameof(TenantFilter)
                : nameof(SoftDeleteFilter);
            GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(type).Invoke(this, [b]);
        }
    }

    private void SoftDeleteFilter<T>(ModelBuilder b) where T : BaseEntity =>
        b.Entity<T>().HasQueryFilter(e => !e.IsDeleted);

    private void TenantFilter<T>(ModelBuilder b) where T : BaseEntity, ITenantOwned =>
        b.Entity<T>().HasQueryFilter(e => !e.IsDeleted && (SeeAllTenants || e.TenantId == CurrentTenantId));

    private void SchoolFilter<T>(ModelBuilder b) where T : BaseEntity, ISchoolOwned =>
        b.Entity<T>().HasQueryFilter(e => !e.IsDeleted
            && (SeeAllTenants || e.TenantId == CurrentTenantId)
            && (CurrentSchoolId == null || e.SchoolId == CurrentSchoolId));

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        CaptureAuditTrail();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditRules();
        CaptureAuditTrail();
        return base.SaveChanges();
    }

    private static readonly HashSet<string> HiddenFields =
    [
        nameof(AppUser.PasswordHash), nameof(AppUser.NormalizedEmail), nameof(AppUser.FailedLoginCount), nameof(AppUser.LockoutEndUtc),
        nameof(BaseEntity.UpdatedAtUtc), nameof(BaseEntity.UpdatedBy), nameof(BaseEntity.CreatedAtUtc), nameof(BaseEntity.CreatedBy),
        nameof(BaseEntity.DeletedAtUtc), nameof(StoredFile.StoragePath)
    ];

    /// <summary>
    /// Records one audit row per created, changed or deleted entity, with the changed fields.
    /// Only changes made by a signed-in user are recorded (seeding and sign-in bookkeeping are skipped).
    /// </summary>
    private void CaptureAuditTrail()
    {
        if (!currentUser.IsAuthenticated) return;

        var entries = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            var deleted = entry.State == EntityState.Modified && entry.Property(nameof(BaseEntity.IsDeleted)).IsModified && entry.Entity.IsDeleted;
            var action = entry.State == EntityState.Added ? AuditAction.Created : deleted ? AuditAction.Deleted : AuditAction.Updated;

            Dictionary<string, object?>? changes = null;
            if (action == AuditAction.Updated)
            {
                changes = entry.Properties
                    .Where(p => p.IsModified && !HiddenFields.Contains(p.Metadata.Name) && !Equals(p.OriginalValue, p.CurrentValue))
                    .ToDictionary(p => p.Metadata.Name, p => (object?)new { old = p.OriginalValue, @new = p.CurrentValue });
                if (changes.Count == 0) continue; // only bookkeeping fields changed
            }

            var type = entry.Metadata.ClrType.Name;
            var label = DisplayName(entry.Entity);
            AuditLogs.Add(new AuditLog
            {
                TimestampUtc = now,
                TenantId = entry.Entity is ITenantOwned t ? t.TenantId : entry.Entity is AppUser u ? u.TenantId : currentUser.TenantId,
                SchoolId = entry.Entity is ISchoolOwned s ? s.SchoolId : currentUser.SchoolId,
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                Action = action,
                EntityType = type,
                EntityId = entry.Entity.Id.ToString(),
                Summary = $"{action} {Humanize(type)}{(label is null ? "" : $" \"{label}\"")}",
                Changes = changes is null ? null : System.Text.Json.JsonSerializer.Serialize(changes),
                IpAddress = currentUser.IpAddress
            });
        }
    }

    private static string? DisplayName(BaseEntity e)
    {
        var text = e switch
        {
            Person p => p.FullName,
            AppUser u => u.Email,
            Invoice i => i.Number,
            _ => (string?)(e.GetType().GetProperty("Name") ?? e.GetType().GetProperty("Title") ?? e.GetType().GetProperty("FileName"))?.GetValue(e)
        };
        return text is { Length: > 120 } ? text[..120] : text;
    }

    private static string Humanize(string type) =>
        string.Concat(type.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>Stamps audit fields, turns deletes into soft deletes and blocks cross-tenant writes.</summary>
    private void ApplyAuditRules()
    {
        var now = DateTime.UtcNow;
        var userId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy ??= userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }

            if (entry.Entity is ITenantOwned owned && entry.State is EntityState.Added or EntityState.Modified)
            {
                if (owned.TenantId == Guid.Empty && CurrentTenantId is { } tid)
                    owned.TenantId = tid;

                if (currentUser.IsAuthenticated && !currentUser.IsSuperAdmin && owned.TenantId != CurrentTenantId)
                    throw new ForbiddenException("Cross-tenant write blocked.");
            }
        }
    }
}
