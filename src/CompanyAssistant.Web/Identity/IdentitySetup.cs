using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;

namespace CompanyAssistant.Web.Identity;

internal static partial class IdentitySetup
{
    public static void AddEntraIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider =>
            EntraIdentitySettings.LoadAndValidate(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddHostedService<IdentityStartupValidation>();

        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("CompanyAssistant");
        var keyPath = builder.Configuration["DataProtection:KeyPath"];
        if (!string.IsNullOrWhiteSpace(keyPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-CompanyAssistant.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.IsEssential = true;
        });

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
        builder.Services.Configure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            options => ConfigureSessionCookie(options));
        builder.Services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<EntraIdentitySettings>((options, settings) => ConfigureOpenIdConnect(options, settings));

        builder.Services.AddAuthorization(options =>
        {
            var assignedUserPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AssignedUserRequirement())
                .Build();
            options.DefaultPolicy = assignedUserPolicy;
            options.AddPolicy(IdentityPolicies.AssignedUser, assignedUserPolicy);

            foreach (var permission in AppPermissions.All)
            {
                options.AddPolicy(
                    IdentityPolicies.ForPermission(permission),
                    policy => policy.RequireAuthenticatedUser()
                        .AddRequirements(new AssignedUserRequirement(), new PermissionRequirement(permission)));
            }
        });
        builder.Services.AddSingleton<IAuthorizationHandler, AssignedUserAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            foreach (var value in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren())
            {
                if (!IPAddress.TryParse(value.Value, out var address))
                    throw new InvalidOperationException("ForwardedHeaders:KnownProxies entries must be IP addresses.");
                options.KnownProxies.Add(address);
            }
        });
    }
}
