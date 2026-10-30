using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyAssistant.Web.Tests;

public sealed class CsrfTests(TestApplicationFactory factory) : IClassFixture<TestApplicationFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task Logout_without_valid_csrf_token_is_rejected(string? token)
    {
        using var client = CreateClient();
        using var request = TestApplicationFactory.Request(HttpMethod.Post, "/api/auth/logout");
        if (token is not null)
            request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task Logout_with_valid_csrf_cookie_and_header_signs_out_both_schemes()
    {
        using var client = CreateClient();
        using var tokenResponse = await client.SendAsync(TestApplicationFactory.Request(
            HttpMethod.Get, "/api/auth/antiforgery"));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var cookies = tokenResponse.Headers.GetValues("Set-Cookie").ToArray();
        var requestCookie = cookies.Single(value => value.StartsWith(
            "__Host-CompanyAssistant.XSRF=", StringComparison.Ordinal));
        var encodedToken = requestCookie.Split(';')[0].Split('=', 2)[1];
        var token = Uri.UnescapeDataString(encodedToken);

        using var logout = TestApplicationFactory.Request(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://login.microsoftonline.com/test/logout", response.Headers.Location?.ToString());
        var logoutCookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(logoutCookies, value => value.StartsWith(
            "__Host-CompanyAssistant.Session=", StringComparison.Ordinal));
        Assert.DoesNotContain("access_token", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
