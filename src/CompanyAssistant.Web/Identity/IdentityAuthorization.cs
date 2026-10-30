using Microsoft.AspNetCore.Authorization;

namespace CompanyAssistant.Web.Identity;

internal static class IdentityPolicies
{
    public const string AssignedUser = "assigned-user";
    public static string ForPermission(string permission) => $"permission:{permission}";
}

internal sealed class AssignedUserRequirement : IAuthorizationRequirement;
internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class AssignedUserAuthorizationHandler(EntraIdentitySettings settings)
    : AuthorizationHandler<AssignedUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AssignedUserRequirement requirement)
    {
        if (EntraPrincipalValidator.IsValidAssignedUser(context.User, settings.TenantId))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (AppRoles.All.Any(role => context.User.IsInRole(role)
            && RolePermissions.HasPermission(role, requirement.Permission)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
