using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.Api.Security;
using SmartSchool.Application.Services;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants")]
public class TenantsController(TenantService tenants) : ControllerBase
{
    [HasPermission(Permissions.Tenants.View)]
    [HttpGet]
    public Task<PagedResult<TenantDto>> List([FromQuery] PagedQuery query, CancellationToken ct) => tenants.ListAsync(query, ct);

    [HasPermission(Permissions.Tenants.View)]
    [HttpGet("lookup")]
    public Task<List<LookupItem>> Lookup(CancellationToken ct) => tenants.LookupAsync(ct);

    [HasPermission(Permissions.Tenants.View)]
    [HttpGet("{id:guid}")]
    public Task<TenantEditDto> Get(Guid id, CancellationToken ct) => tenants.GetAsync(id, ct);

    [HasPermission(Permissions.Tenants.Create)]
    [HttpPost]
    public async Task<object> Create(TenantEditDto dto, CancellationToken ct) => new { id = await tenants.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Tenants.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, TenantEditDto dto, CancellationToken ct)
    {
        await tenants.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Tenants.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tenants.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/schools")]
public class SchoolsController(SchoolService schools) : ControllerBase
{
    [HasPermission(Permissions.Schools.View)]
    [HttpGet]
    public Task<PagedResult<SchoolDto>> List([FromQuery] PagedQuery query, CancellationToken ct) => schools.ListAsync(query, ct);

    [HttpGet("lookup")]
    public Task<List<LookupItem>> Lookup([FromQuery] Guid? tenantId, CancellationToken ct) => schools.LookupAsync(tenantId, ct);

    [HasPermission(Permissions.Schools.View)]
    [HttpGet("{id:guid}")]
    public Task<SchoolEditDto> Get(Guid id, CancellationToken ct) => schools.GetAsync(id, ct);

    [HasPermission(Permissions.Schools.Create)]
    [HttpPost]
    public async Task<object> Create(SchoolEditDto dto, CancellationToken ct) => new { id = await schools.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Schools.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SchoolEditDto dto, CancellationToken ct)
    {
        await schools.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Schools.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await schools.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController(UserService users) : ControllerBase
{
    [HasPermission(Permissions.Users.View)]
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] PagedQuery query, [FromQuery] RoleType? role, CancellationToken ct) =>
        users.ListAsync(query, role, ct);

    [HasPermission(Permissions.Users.View)]
    [HttpGet("{id:guid}")]
    public Task<UserEditDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HasPermission(Permissions.Users.Create)]
    [HttpPost]
    public async Task<object> Create(UserEditDto dto, CancellationToken ct) => new { id = await users.CreateAsync(dto, ct) };

    [HasPermission(Permissions.Users.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UserEditDto dto, CancellationToken ct)
    {
        await users.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Users.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/roles")]
public class RolesController(RoleService roles) : ControllerBase
{
    [HasPermission(Permissions.Roles.View)]
    [HttpGet]
    public Task<List<RoleDto>> List(CancellationToken ct) => roles.ListAsync(ct);

    [HasPermission(Permissions.Users.View)]
    [HttpGet("assignable")]
    public Task<List<RoleDto>> Assignable(CancellationToken ct) => roles.AssignableAsync(ct);

    [HasPermission(Permissions.Roles.View)]
    [HttpGet("permissions")]
    public IReadOnlyList<PermissionInfo> AllPermissions() => Permissions.All;

    [HasPermission(Permissions.Roles.Manage)]
    [HttpPost]
    public async Task<object> CreateCustom(RoleEditDto dto, CancellationToken ct) => new { id = await roles.CreateCustomAsync(dto, ct) };

    [HasPermission(Permissions.Roles.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCustom(Guid id, RoleEditDto dto, CancellationToken ct)
    {
        await roles.UpdateCustomAsync(id, dto, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Roles.Manage)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCustom(Guid id, CancellationToken ct)
    {
        await roles.DeleteCustomAsync(id, ct);
        return NoContent();
    }

    [HasPermission(Permissions.Roles.Manage)]
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> UpdatePermissions(Guid id, RolePermissionsUpdateDto dto, CancellationToken ct)
    {
        await roles.UpdatePermissionsAsync(id, dto.Permissions, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardStatsDto> Get(CancellationToken ct) => dashboard.GetAsync(ct);
}
