using System.Security.Claims;
using SmartSchool.Application.Common;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Security;

/// <summary>
/// Reads the caller from the JWT and resolves the active context. Tenant users are pinned to the
/// tenant in their token; only a SuperAdmin may pick a tenant through the X-Tenant-Id header.
/// School-scoped users can only select one of their assigned schools.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue(AppClaims.UserId));

    public Guid? SessionId => ParseGuid(Principal?.FindFirstValue(AppClaims.SessionId));

    public RoleType? RoleType =>
        int.TryParse(Principal?.FindFirstValue(AppClaims.RoleType), out var v) && Enum.IsDefined(typeof(RoleType), v)
            ? (RoleType)v
            : null;

    public bool IsSuperAdmin => IsAuthenticated && RoleType == Shared.Enums.RoleType.SuperAdmin;

    public Guid? TenantId => IsSuperAdmin
        ? ParseGuid(accessor.HttpContext?.Request.Headers[AppHeaders.TenantId].FirstOrDefault())
        : ParseGuid(Principal?.FindFirstValue(AppClaims.TenantId));

    public IReadOnlyCollection<Guid> AllowedSchoolIds =>
        Principal?.FindAll(AppClaims.SchoolIds).Select(c => ParseGuid(c.Value)).OfType<Guid>().ToList() ?? [];

    public Guid? SchoolId
    {
        get
        {
            if (!IsAuthenticated) return null;
            var requested = ParseGuid(accessor.HttpContext?.Request.Headers[AppHeaders.SchoolId].FirstOrDefault());

            if (RoleType?.IsSchoolScoped() == true)
            {
                var allowed = AllowedSchoolIds;
                if (requested is { } r && allowed.Contains(r)) return r;
                // Fall back to the first assigned school; Guid.Empty matches nothing if none are assigned.
                return allowed.Count > 0 ? allowed.First() : Guid.Empty;
            }

            // A SuperAdmin can only narrow to a school after choosing a tenant.
            if (IsSuperAdmin && TenantId is null) return null;
            return requested;
        }
    }

    public Guid? AcademicYearId => IsAuthenticated
        ? ParseGuid(accessor.HttpContext?.Request.Headers[AppHeaders.AcademicYearId].FirstOrDefault())
        : null;

    public string? UserName => Principal?.FindFirstValue(AppClaims.Name);

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool HasPermission(string permission) =>
        IsSuperAdmin || Principal?.HasClaim(AppClaims.Permission, permission) == true;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}
