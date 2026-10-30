using CompanyAssistant.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyAssistant.Web.Tests;

public sealed class SecurityTests(TestApplicationFactory factory) : IClassFixture<TestApplicationFactory>
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/policies?lang=en", "/policies?lang=en")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example/", "/")]
    [InlineData("/\\evil.example/", "/")]
    [InlineData("/%2f%2fevil.example/", "/")]
    [InlineData("/%0d%0aLocation:%20https://evil.example/", "/")]
    [InlineData("javascript:alert(1)", "/")]
    public void Return_urls_are_local_and_canonical(string? value, string expected) =>
        Assert.Equal(expected, SafeReturnUrl.OrDefault(value));

    [Theory]
    [InlineData("AzureAd:Instance", "http://login.microsoftonline.com/")]
    [InlineData("AzureAd:Instance", "https://login.microsoftonline.com/common/")]
    [InlineData("AzureAd:TenantId", "not-a-guid")]
    [InlineData("AzureAd:TenantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("AzureAd:ClientId", "")]
    [InlineData("AzureAd:ClientSecret", "")]
    [InlineData("AzureAd:CallbackPath", "https://evil.example/callback")]
    [InlineData("AzureAd:SignedOutCallbackPath", "/other")]
    [InlineData("AzureAd:SignedOutCallbackPath", "/signout-oidc")]
    [InlineData("AzureAd:RemoteSignOutPath", "/other")]
    [InlineData("AzureAd:RemoteSignOutPath", "/signout-callback-oidc")]
    public void Missing_or_invalid_identity_configuration_fails_closed(string key, string value)
    {
        var values = TestApplicationFactory.TestConfiguration();
        values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() => EntraIdentitySettings.LoadAndValidate(configuration));
    }

    [Fact]
    public void Production_host_without_identity_settings_fails_during_startup()
    {
        using var productionFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "",
                    ["AzureAd:ClientId"] = "",
                    ["AzureAd:ClientSecret"] = ""
                }));
        });
        var exception = Assert.ThrowsAny<Exception>(() => productionFactory.CreateClient());
        Assert.Contains("AzureAd:TenantId", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_host_has_no_test_identity_scheme()
    {
        using var productionFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(TestApplicationFactory.TestConfiguration()));
        });
        var schemes = await productionFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        Assert.DoesNotContain(schemes, scheme => scheme.Name == TestApplicationFactory.Scheme);
        var authentication = productionFactory.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authentication.DefaultScheme);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authentication.DefaultChallengeScheme);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, authentication.DefaultForbidScheme);
    }

    [Fact]
    public void Cookies_and_oidc_options_use_secure_settings()
    {
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.False(oidc.SaveTokens);
        Assert.True(oidc.UsePkce);
        Assert.DoesNotContain("offline_access", oidc.Scope);
        Assert.Equal(TestApplicationFactory.ClientId, oidc.TokenValidationParameters.ValidAudience);
        Assert.Equal("roles", oidc.TokenValidationParameters.RoleClaimType);
        Assert.Equal(CookieSecurePolicy.Always, oidc.CorrelationCookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, oidc.NonceCookie.SecurePolicy);

        var cookies = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.True(cookies.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, cookies.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookies.Cookie.SameSite);
        Assert.Equal("__Host-CompanyAssistant.Session", cookies.Cookie.Name);

        var antiforgery = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("X-CSRF-TOKEN", antiforgery.HeaderName);
        Assert.True(antiforgery.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, antiforgery.Cookie.SecurePolicy);
    }
}
