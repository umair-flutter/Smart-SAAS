using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Domain.Entities;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Application.Services;

public class UserService(IAppDbContext db, ICurrentUser current, IPasswordService passwords, PlanLimitService limits)
{
    private IQueryable<AppUser> Visible()
    {
        var q = db.Users.AsQueryable();
        if (current.RoleType?.IsSchoolScoped() == true)
        {
            var allowed = current.AllowedSchoolIds.ToList();
            q = q.Where(u => u.Schools.Any(s => allowed.Contains(s.SchoolId)));
        }
        if (current.SchoolId is { } schoolId)
            q = q.Where(u => u.Schools.Any(s => s.SchoolId == schoolId) || u.Role!.Type == RoleType.TenantAdmin);
        return q;
    }

    public async Task<PagedResult<UserDto>> ListAsync(PagedQuery query, RoleType? roleType, CancellationToken ct)
    {
        var q = Visible().AsNoTracking();
        if (roleType is not null) q = q.Where(u => u.Role!.Type == roleType);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(u => u.Email.Contains(s) || u.FirstName.Contains(s) || u.LastName.Contains(s));
        }

        q = query.SortBy?.ToLowerInvariant() switch
        {
            "email" => q.OrderByDirection(u => u.Email, query.SortDesc),
            "role" => q.OrderByDirection(u => u.Role!.Type, query.SortDesc),
            "tenant" => q.OrderByDirection(u => u.Tenant!.Name, query.SortDesc),
            "lastlogin" => q.OrderByDirection(u => u.LastLoginUtc, query.SortDesc),
            _ => q.OrderByDirection(u => u.FirstName, query.SortDesc).ThenBy(u => u.LastName)
        };

        return await q.Select(u => new UserDto
        {
            Id = u.Id,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            RoleId = u.RoleId,
            RoleName = u.Role!.Name,
            RoleType = u.Role.Type,
            TenantId = u.TenantId,
            TenantName = u.Tenant != null ? u.Tenant.Name : null,
            SchoolIds = u.Schools.Select(s => s.SchoolId).ToList(),
            SchoolNames = u.Schools.Select(s => s.School!.Name).ToList(),
            IsActive = u.IsActive,
            LastLoginUtc = u.LastLoginUtc,
            PreferredLanguage = u.PreferredLanguage
        }).ToPagedResultAsync(query, ct);
    }

    public async Task<UserEditDto> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await Find(id, ct);
        return new UserEditDto
        {
            TenantId = u.TenantId,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Phone = u.Phone,
            RoleId = u.RoleId,
            SchoolIds = u.Schools.Select(s => s.SchoolId).ToList(),
            IsActive = u.IsActive,
            PreferredLanguage = u.PreferredLanguage
        };
    }

    public async Task<Guid> CreateAsync(UserEditDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw new ValidationException(nameof(dto.Password), "A password is required for new users.");

        var role = await ResolveAssignableRole(dto.RoleId, ct);
        var tenantId = ResolveTenant(role, dto.TenantId);
        if (tenantId is { } limitTenant) await limits.EnsureCanAddUserAsync(limitTenant, ct);
        await EnsureUniqueEmail(dto.Email, null, ct);

        var user = new AppUser { TenantId = tenantId, PasswordHash = passwords.Hash(dto.Password) };
        Apply(user, dto, role);
        await SetSchools(user, role, tenantId, dto.SchoolIds, ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    public async Task UpdateAsync(Guid id, UserEditDto dto, CancellationToken ct)
    {
        var user = await Find(id, ct);
        var role = await ResolveAssignableRole(dto.RoleId, ct);
        if (user.Id == current.UserId && role.Id != user.RoleId)
            throw new ValidationException(nameof(dto.RoleId), "You cannot change your own role.");
        await EnsureUniqueEmail(dto.Email, id, ct);

        Apply(user, dto, role);
        if (!string.IsNullOrWhiteSpace(dto.Password))
            user.PasswordHash = passwords.Hash(dto.Password);
        await SetSchools(user, role, user.TenantId, dto.SchoolIds, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (id == current.UserId) throw new ValidationException("id", "You cannot delete your own account.");
        var user = await Find(id, ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AppUser> Find(Guid id, CancellationToken ct) =>
        await Visible().Include(u => u.Schools).Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("User", id);

    /// <summary>Callers may only hand out roles narrower than their own (TenantAdmins may also add TenantAdmins).</summary>
    private async Task<Role> ResolveAssignableRole(Guid roleId, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new ValidationException(nameof(UserEditDto.RoleId), "Select a valid role.");
        var mine = current.RoleType ?? throw new ForbiddenException();
        var allowed = mine == RoleType.SuperAdmin
            || role.Type > mine
            || (mine == RoleType.TenantAdmin && role.Type == RoleType.TenantAdmin);
        if (!allowed) throw new ForbiddenException("You cannot assign this role.");
        return role;
    }

    private Guid? ResolveTenant(Role role, Guid? requested)
    {
        if (role.Type == RoleType.SuperAdmin) return null;
        if (current.IsSuperAdmin)
            return requested ?? current.TenantId
                ?? throw new ValidationException(nameof(UserEditDto.TenantId), "Select the tenant this user belongs to.");
        return current.TenantId ?? throw new ForbiddenException();
    }

    private async Task SetSchools(AppUser user, Role role, Guid? tenantId, List<Guid> schoolIds, CancellationToken ct)
    {
        var ids = role.Type.IsSchoolScoped() ? schoolIds.Distinct().ToList() : [];
        if (role.Type.IsSchoolScoped() && ids.Count == 0)
            throw new ValidationException(nameof(UserEditDto.SchoolIds), "Assign at least one school for this role.");

        if (ids.Count > 0)
        {
            var valid = await db.Schools.Where(s => ids.Contains(s.Id) && s.TenantId == tenantId).Select(s => s.Id).ToListAsync(ct);
            if (valid.Count != ids.Count)
                throw new ValidationException(nameof(UserEditDto.SchoolIds), "One or more schools do not belong to this tenant.");
            if (current.RoleType?.IsSchoolScoped() == true && ids.Except(current.AllowedSchoolIds).Any())
                throw new ForbiddenException("You can only assign your own schools.");
        }

        user.Schools.RemoveAll(s => !ids.Contains(s.SchoolId));
        foreach (var id in ids.Where(id => user.Schools.All(s => s.SchoolId != id)))
            user.Schools.Add(new UserSchool { UserId = user.Id, SchoolId = id });
    }

    private async Task EnsureUniqueEmail(string email, Guid? exceptId, CancellationToken ct)
    {
        var n = email.NormalizeKey();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == n && u.Id != exceptId && !u.IsDeleted, ct))
            throw new ValidationException(nameof(UserEditDto.Email), "This email address is already registered.");
    }

    private static void Apply(AppUser u, UserEditDto dto, Role role)
    {
        u.Email = dto.Email.Trim();
        u.NormalizedEmail = dto.Email.NormalizeKey();
        u.FirstName = dto.FirstName.Trim();
        u.LastName = dto.LastName.Trim();
        u.Phone = dto.Phone?.Trim();
        u.RoleId = role.Id;
        u.IsActive = dto.IsActive;
        u.PreferredLanguage = dto.PreferredLanguage;
    }
}
