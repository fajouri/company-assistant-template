using CompanyAssistant.Web.Identity;

namespace CompanyAssistant.Web.Knowledge;

internal static class AccessPolicyMapping
{
    // Maps the caller's named permissions (derived from their app roles by the
    // application, never from model/query parameters) to the access-policy labels
    // they are allowed to read. The resulting list is passed to the database so
    // authorization is enforced inside the SQL query, before vector ranking/limit.
    public static IReadOnlyCollection<string> AllowedFor(IReadOnlyCollection<string> permissions)
    {
        var allowed = new List<string>(2);
        if (permissions.Contains(AppPermissions.AssistantUse))
            allowed.Add(AccessPolicies.Employee);
        if (permissions.Contains(AppPermissions.HrDataRead))
            allowed.Add(AccessPolicies.Hr);
        return allowed;
    }
}
