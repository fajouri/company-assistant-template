using System.Net;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyAssistant.Web.Tests;

public sealed class SignOutCallbackTests(TestApplicationFactory factory) : IClassFixture<TestApplicationFactory>
{
    [Fact]
    public void Login_signed_out_callback_and_remote_sign_out_paths_are_distinct()
    {
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Equal("/signin-oidc", oidc.CallbackPath.Value);
        Assert.Equal("/signout-callback-oidc", oidc.SignedOutCallbackPath.Value);
        Assert.Equal("/signout-oidc", oidc.RemoteSignOutPath.Value);
        Assert.NotEqual(oidc.SignedOutCallbackPath.Value, oidc.RemoteSignOutPath.Value);
    }

    [Fact]
    public async Task Logout_redirects_to_provider_and_callback_returns_to_root()
    {
        using var client = CreateClient();

        // Obtain a valid antiforgery token.
        using var tokenResponse = await client.SendAsync(TestApplicationFactory.Request(
            HttpMethod.Get, "/api/auth/antiforgery"));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var cookies = tokenResponse.Headers.GetValues("Set-Cookie").ToArray();
        var requestCookie = cookies.Single(value => value.StartsWith(
            "__Host-CompanyAssistant.XSRF=", StringComparison.Ordinal));
        var token = Uri.UnescapeDataString(requestCookie.Split(';')[0].Split('=', 2)[1]);

        // Initiate logout and capture the redirect to the identity provider.
        using var logout = TestApplicationFactory.Request(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", token);
        using var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        var location = logoutResponse.Headers.Location;
        Assert.NotNull(location);
        Assert.StartsWith("https://login.microsoftonline.com/test/logout", location!.ToString(), StringComparison.Ordinal);

        // Verify the generated post-logout redirect URI is the distinct signed-out callback.
        var query = QueryHelpers.ParseQuery(location.Query);
        var postLogoutRedirectUri = query["post_logout_redirect_uri"].Single();
        Assert.Equal("https://localhost/signout-callback-oidc", postLogoutRedirectUri);
        var state = query["state"].Single();
        Assert.False(string.IsNullOrEmpty(state));

        // Simulate the provider returning to the callback, preserving the generated state.
        var callbackUrl = $"{postLogoutRedirectUri}?state={Uri.EscapeDataString(state)}";
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callbackUrl);
        using var callbackResponse = await client.SendAsync(callbackRequest);

        // The final redirect must return to the application root.
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.Equal("/", callbackResponse.Headers.GetValues("Location").Single());
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
