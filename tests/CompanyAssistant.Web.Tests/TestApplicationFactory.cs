using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CompanyAssistant.Web.Tests;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    public const string Scheme = "TestIdentity";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string OtherTenantId = "22222222-2222-2222-2222-222222222222";
    public const string ClientId = "33333333-3333-3333-3333-333333333333";
    public const string ObjectId = "44444444-4444-4444-4444-444444444444";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(TestConfiguration()));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = Scheme;
                    options.DefaultAuthenticateScheme = Scheme;
                    options.DefaultChallengeScheme = Scheme;
                    options.DefaultForbidScheme = Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>(Scheme, _ => { });
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://login.microsoftonline.com/test/authorize",
                    TokenEndpoint = "https://login.microsoftonline.com/test/token",
                    EndSessionEndpoint = "https://login.microsoftonline.com/test/logout"
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
        });
    }

    public static Dictionary<string, string?> TestConfiguration() => new()
    {
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = TenantId,
        ["AzureAd:ClientId"] = ClientId,
        ["AzureAd:ClientSecret"] = "fictional-test-configuration-not-a-real-secret",
        ["AzureAd:CallbackPath"] = "/signin-oidc",
        ["AzureAd:SignedOutCallbackPath"] = "/signout-oidc",
        ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "company-assistant-tests", Guid.NewGuid().ToString("N"))
    };

    public static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string role = "Employee",
        string? tenantId = TenantId,
        string? objectId = ObjectId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Tenant", tenantId);
        request.Headers.Add("X-Test-Object", objectId);
        request.Headers.Add("X-Test-Roles", role);
        request.Headers.Add("X-Test-Name", "Fictional Test User");
        return request;
    }
}

internal sealed class TestIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("X-Test-Object"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("tid", Request.Headers["X-Test-Tenant"].ToString()),
            new("oid", Request.Headers["X-Test-Object"].ToString()),
            new("name", Request.Headers["X-Test-Name"].ToString())
        };
        claims.AddRange(Request.Headers["X-Test-Roles"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => new Claim("roles", role)));
        var identity = new ClaimsIdentity(claims, TestApplicationFactory.Scheme, "name", "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestApplicationFactory.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
