using SmartSchool.Domain.Common;

namespace SmartSchool.Domain.Entities;

public class School : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PrincipalName { get; set; }
    public bool IsActive { get; set; } = true;
}
