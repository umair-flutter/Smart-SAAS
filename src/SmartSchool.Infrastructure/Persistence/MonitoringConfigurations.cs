using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartSchool.Domain.Entities;

namespace SmartSchool.Infrastructure.Persistence;

internal sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(4000);
        b.Property(x => x.Link).HasMaxLength(300);
        b.Property(x => x.ActionLabel).HasMaxLength(60);
        b.Property(x => x.SenderName).HasMaxLength(200);
        b.Property(x => x.AudienceLabel).HasMaxLength(200);
        b.HasMany(x => x.Recipients).WithOne(r => r.Notification).HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        b.HasIndex(x => x.SenderUserId);
    }
}

internal sealed class NotificationRecipientConfig : IEntityTypeConfiguration<NotificationRecipient>
{
    public void Configure(EntityTypeBuilder<NotificationRecipient> b)
    {
        b.HasKey(x => new { x.NotificationId, x.UserId });
        b.HasIndex(x => new { x.UserId, x.ReadAtUtc });
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.UserName).HasMaxLength(200);
        b.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Summary).HasMaxLength(400);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.TenantId, x.TimestampUtc });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class TechnicalLogConfig : IEntityTypeConfiguration<TechnicalLog>
{
    public void Configure(EntityTypeBuilder<TechnicalLog> b)
    {
        b.Property(x => x.Level).HasMaxLength(20).IsRequired();
        b.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        b.Property(x => x.SourceContext).HasMaxLength(300);
        b.Property(x => x.RequestPath).HasMaxLength(400);
        b.Property(x => x.TraceId).HasMaxLength(100);
        b.HasIndex(x => x.TimestampUtc);
    }
}

internal sealed class PageViewConfig : IEntityTypeConfiguration<PageView>
{
    public void Configure(EntityTypeBuilder<PageView> b)
    {
        b.Property(x => x.Path).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.TimestampUtc });
    }
}

internal sealed class StatusOptionConfig : IEntityTypeConfiguration<StatusOption>
{
    public void Configure(EntityTypeBuilder<StatusOption> b)
    {
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Color).HasMaxLength(7).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Category, x.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
