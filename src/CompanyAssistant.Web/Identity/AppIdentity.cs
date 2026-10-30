using System.Security.Claims;

namespace CompanyAssistant.Web.Identity;

internal static class AppRoles
{
    public const string Employee = "Employee";
    public const string Hr = "HR";
    public const string TechnicalAdmin = "TechnicalAdmin";

    public static readonly IReadOnlyList<string> All = [Employee, Hr, TechnicalAdmin];
}

internal static class AppPermissions
{
    public const string AssistantUse = "assistant.use";
    public const string HrDataRead = "hr.data.read";
    public const string TechnicalAdministration = "technical.administration";

    public static readonly IReadOnlyList<string> All =
        [AssistantUse, HrDataRead, TechnicalAdministration];
}

internal static class RolePermissions
{
    public static bool HasPermission(string role, string permission) => role switch
    {
        AppRoles.Employee => permission == AppPermissions.AssistantUse,
        AppRoles.Hr => permission == AppPermissions.HrDataRead,
        AppRoles.TechnicalAdmin => permission == AppPermissions.TechnicalAdministration,
        _ => false
    };

    public static string[] For(ClaimsPrincipal user) => AppPermissions.All
        .Where(permission => AppRoles.All.Any(role => user.IsInRole(role) && HasPermission(role, permission)))
        .ToArray();
}

internal static class EntraPrincipalValidator
{
    public static bool IsValidAssignedUser(ClaimsPrincipal user, Guid expectedTenantId) =>
        user.Identity?.IsAuthenticated == true
        && TryGetNonEmptyGuid(user, "tid", out var tenantId)
        && tenantId == expectedTenantId
        && TryGetNonEmptyGuid(user, "oid", out _)
        && AppRoles.All.Any(user.IsInRole);

    public static bool TryGetNonEmptyGuid(ClaimsPrincipal user, string claimType, out Guid value) =>
        Guid.TryParse(user.FindFirst(claimType)?.Value, out value) && value != Guid.Empty;
}

internal static class SafeReturnUrl
{
    public static string OrDefault(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "/";

        // The shell only needs canonical local URLs. Reject encoding and backslashes so
        // browsers and proxies cannot disagree about separators, hosts or control characters.
        if (value.Length > 2048
            || value[0] != '/'
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('%')
            || value.Any(char.IsControl))
            return "/";

        return value;
    }
}
