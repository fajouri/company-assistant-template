using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CompanyAssistant.Web.Identity;

internal static partial class IdentitySetup
{
    private static void ConfigureSessionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-CompanyAssistant.Session";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // Lax permits the top-level OIDC return redirect but not cross-site POST cookies.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;
        options.LoginPath = "/api/auth/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context => HandleApiRedirect(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => HandleApiRedirect(context, StatusCodes.Status403Forbidden);
    }

    private static Task HandleApiRedirect(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context,
        int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    private static void ConfigureOpenIdConnect(OpenIdConnectOptions options, EntraIdentitySettings settings)
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        // The login callback, post-logout callback and front-channel logout paths must stay
        // distinct: OpenIdConnectHandler routes RemoteSignOutPath before SignedOutCallbackPath.
        options.CallbackPath = IdentityPaths.SignInCallbackPath;
        options.SignedOutCallbackPath = IdentityPaths.SignedOutCallbackPath;
        options.RemoteSignOutPath = IdentityPaths.RemoteSignOutPath;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        ConfigureRemoteCookie(options.CorrelationCookie, "__Host-CompanyAssistant.Correlation");
        ConfigureRemoteCookie(options.NonceCookie, "__Host-CompanyAssistant.Nonce");

        options.TokenValidationParameters.ValidAudience = settings.ClientId.ToString();
        options.TokenValidationParameters.ValidIssuer = settings.Issuer;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";

        options.Events.OnTokenValidated = context =>
        {
            if (context.Principal is null
                || !EntraPrincipalValidator.IsValidAssignedUser(context.Principal, settings.TenantId))
                context.Fail("The signed-in user does not have a valid tenant, object identifier and app-role assignment.");
            return Task.CompletedTask;
        };
        options.Events.OnAuthenticationFailed = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    }

    private static void ConfigureRemoteCookie(CookieBuilder cookie, string name)
    {
        cookie.Name = name;
        cookie.Path = "/";
        cookie.HttpOnly = true;
        cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.SameSite = SameSiteMode.None;
        cookie.IsEssential = true;
    }
}
