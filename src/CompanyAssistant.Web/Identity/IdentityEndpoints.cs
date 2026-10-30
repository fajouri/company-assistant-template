using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;

namespace CompanyAssistant.Web.Identity;

internal static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/login", (string? returnUrl) =>
                Results.Challenge(
                    new AuthenticationProperties { RedirectUri = SafeReturnUrl.OrDefault(returnUrl) },
                    [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        endpoints.MapGet("/api/session", (HttpContext context, EntraIdentitySettings settings) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.User.Identity?.IsAuthenticated != true)
                return Results.Ok(new { isAuthenticated = false });
            if (!EntraPrincipalValidator.IsValidAssignedUser(context.User, settings.TenantId))
                return Results.Forbid();

            var roles = AppRoles.All.Where(context.User.IsInRole).ToArray();
            return Results.Ok(new
            {
                isAuthenticated = true,
                user = new
                {
                    tenantId = context.User.FindFirst("tid")!.Value,
                    objectId = context.User.FindFirst("oid")!.Value,
                    displayName = context.User.FindFirst("name")?.Value ?? "Signed-in user",
                    roles,
                    permissions = RolePermissions.For(context.User)
                }
            });
        }).AllowAnonymous();

        endpoints.MapGet("/api/auth/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Cookies.Append(
                "__Host-CompanyAssistant.XSRF",
                tokens.RequestToken!,
                new CookieOptions
                {
                    HttpOnly = false,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    IsEssential = true
                });
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok();
        }).RequireAuthorization(IdentityPolicies.AssignedUser);

        endpoints.MapPost("/api/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
                return Results.BadRequest(new { error = "A valid antiforgery token is required." });

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        }).RequireAuthorization(IdentityPolicies.AssignedUser);

        endpoints.MapGet("/api/protected", () => new
            {
                enabled = true,
                message = "The chat endpoint is available to authorized users."
            })
            .RequireAuthorization(IdentityPolicies.ForPermission(AppPermissions.AssistantUse));
    }
}
