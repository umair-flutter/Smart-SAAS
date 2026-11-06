namespace SmartSchool.Domain.Common;

/// <summary>Base type for every persisted entity: identity, audit stamps and soft delete.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>Entity that belongs to exactly one tenant (organization).</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>Entity that belongs to one school inside a tenant.</summary>
public interface ISchoolOwned : ITenantOwned
{
    Guid SchoolId { get; set; }
}
