using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Application.Services;

public class RoleService(IAppDbContext db, ICurrentUser current)
{
    public async Task<List<RoleDto>> ListAsync(CancellationToken ct)
    {
        var tenantId = current.TenantId;
        var bypass = current.IsSuperAdmin && tenantId is null;
        return await db.Roles.AsNoTracking()
            .OrderBy(r => r.Type).ThenBy(r => r.Name)
            .Select(r => new RoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Type = r.Type,
                IsSystem = r.IsSystem,
                Permissions = r.Permissions.Select(p => p.Permission).ToList(),
                UserCount = db.Users.Count(u => u.RoleId == r.Id && (bypass || u.TenantId == tenantId))
            })
            .ToListAsync(ct);
    }

    /// <summary>Roles the caller may assign when creating or editing a user.</summary>
    public async Task<List<RoleDto>> AssignableAsync(CancellationToken ct)
    {
        var mine = current.RoleType ?? RoleType.Student;
        var all = await ListAsync(ct);
        return all.Where(r => mine == RoleType.SuperAdmin || r.Type > mine || (mine == RoleType.TenantAdmin && r.Type == RoleType.TenantAdmin))
            .ToList();
    }

    public async Task UpdatePermissionsAsync(Guid roleId, List<string> permissions, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new NotFoundException("Role", roleId);

        if (role.Type == RoleType.SuperAdmin)
            throw new ValidationException("permissions", "The SuperAdmin role always has every permission.");
        if (role.IsSystem && !current.IsSuperAdmin)
            throw new ForbiddenException("Only a SuperAdmin can change system roles.");

        var known = Permissions.All.Select(p => p.Value).ToHashSet();
        // Nobody can grant a permission they do not hold themselves (SuperAdmin holds all).
        var wanted = permissions.Where(known.Contains).Where(current.HasPermission).Distinct().ToHashSet();

        role.Permissions.RemoveAll(p => !wanted.Contains(p.Permission));
        foreach (var p in wanted.Where(p => role.Permissions.All(x => x.Permission != p)))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Creates a tenant-specific role. It behaves like its base access profile (which data it can reach)
    /// and holds only the permissions chosen here. Callers cannot hand out permissions they do not have.
    /// </summary>
    public async Task<Guid> CreateCustomAsync(RoleEditDto dto, CancellationToken ct)
    {
        var tenantId = current.TenantId ?? throw new ValidationException("tenant", "Choose a tenant in the top bar first; custom roles belong to one organization.");
        ValidateType(dto.Type);
        await EnsureUniqueName(dto.Name, null, ct);
        var role = new Role { TenantId = tenantId, Name = dto.Name.Trim(), Description = dto.Description?.Trim(), Type = dto.Type, IsSystem = false };
        role.Permissions.AddRange(AllowedPermissions(dto.Permissions).Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task UpdateCustomAsync(Guid id, RoleEditDto dto, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);
        if (role.IsSystem) throw new ValidationException("role", "Built-in roles cannot be renamed; change their permissions instead.");
        ValidateType(dto.Type);
        await EnsureUniqueName(dto.Name, id, ct);
        if (role.Type != dto.Type && await db.Users.AnyAsync(u => u.RoleId == id, ct))
            throw new ValidationException(nameof(dto.Type), "The access profile cannot change while users have this role.");
        role.Name = dto.Name.Trim();
        role.Description = dto.Description?.Trim();
        role.Type = dto.Type;
        var wanted = AllowedPermissions(dto.Permissions).ToHashSet();
        role.Permissions.RemoveAll(p => !wanted.Contains(p.Permission));
        foreach (var p in wanted.Where(p => role.Permissions.All(x => x.Permission != p)))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteCustomAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);
        if (role.IsSystem) throw new ValidationException("role", "Built-in roles cannot be deleted.");
        if (await db.Users.AnyAsync(u => u.RoleId == id, ct))
            throw new ConflictException("Move the users of this role to another role first.");
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }

    private void ValidateType(RoleType type)
    {
        if (type == RoleType.SuperAdmin || !Enum.IsDefined(type))
            throw new ValidationException("type", "Choose an access profile other than SuperAdmin.");
        var mine = current.RoleType ?? RoleType.Student;
        if (mine != RoleType.SuperAdmin && type < mine)
            throw new ForbiddenException("You cannot create a role wider than your own.");
    }

    private IEnumerable<string> AllowedPermissions(IEnumerable<string> requested)
    {
        var known = Permissions.All.Select(p => p.Value).ToHashSet();
        return requested.Distinct().Where(p => known.Contains(p) && current.HasPermission(p));
    }

    private async Task EnsureUniqueName(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == n && r.Id != exceptId, ct))
            throw new ValidationException("name", "Another role already uses this name.");
    }
}

public class DashboardService(IAppDbContext db, ICurrentUser current, InvoiceService invoices)
{
    public async Task<DashboardStatsDto> GetAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tenantId = current.TenantId;
        var allTenants = current.IsSuperAdmin && tenantId is null;

        var schools = db.Schools.AsQueryable();
        var users = db.Users.AsQueryable();
        if (current.SchoolId is { } sid)
        {
            schools = schools.Where(s => s.Id == sid);
            users = users.Where(u => u.Schools.Any(s => s.SchoolId == sid));
        }

        var stats = new DashboardStatsDto
        {
            Tenants = allTenants ? await db.Tenants.CountAsync(ct) : 1,
            ActiveTenants = allTenants
                ? await db.Tenants.CountAsync(t => t.IsActive && (t.SubscriptionStatus == SubscriptionStatus.Active || t.SubscriptionStatus == SubscriptionStatus.Trial), ct)
                : 1,
            Schools = await schools.CountAsync(ct),
            Users = await users.CountAsync(ct),
            ActiveSessions = await db.UserSessions.CountAsync(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > now && (allTenants || s.TenantId == tenantId), ct)
        };

        stats.Students = await db.Students.CountAsync(s => s.IsActive, ct);
        stats.Teachers = await db.Teachers.CountAsync(t => t.IsActive, ct);
        stats.Classes = await db.Classes.CountAsync(c => c.AcademicYear!.IsCurrent, ct);

        stats.UsersByRole = (await users.GroupBy(u => u.Role!.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key.DisplayName(), x => x.Count);

        stats.RecentLogins = await users.Where(u => u.LastLoginUtc != null)
            .OrderByDescending(u => u.LastLoginUtc).Take(8)
            .Select(u => new RecentLoginDto(u.FirstName + " " + u.LastName, u.Email, u.Role!.Name, u.LastLoginUtc))
            .ToListAsync(ct);

        if (current.HasPermission(Permissions.Billing.View))
            stats.Billing = await invoices.GetStatsAsync(ct);

        return stats;
    }
}
