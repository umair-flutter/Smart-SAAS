using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Security;

namespace SmartSchool.Api.Security;

/// <summary>Requires a module permission, e.g. <c>[HasPermission(Permissions.Schools.Edit)]</c>.</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Builds "perm:xyz" policies on demand so no policy has to be registered by hand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..]))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.HasClaim(AppClaims.Role, nameof(RoleType.SuperAdmin)) || user.HasClaim(AppClaims.Permission, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
