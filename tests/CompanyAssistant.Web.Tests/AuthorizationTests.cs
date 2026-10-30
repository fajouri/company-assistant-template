using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CompanyAssistant.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyAssistant.Web.Tests;

public sealed class AuthorizationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public AuthorizationTests(TestApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_session_is_public_but_protected_endpoint_is_denied()
    {
        using var client = CreateClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.False(session.GetProperty("isAuthenticated").GetBoolean());

        using var response = await client.GetAsync("/api/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Location"));
    }

    [Theory]
    [InlineData("Employee", HttpStatusCode.OK, "assistant.use")]
    [InlineData("HR", HttpStatusCode.Forbidden, "hr.data.read")]
    [InlineData("TechnicalAdmin", HttpStatusCode.Forbidden, "technical.administration")]
    public async Task Roles_map_to_independent_permissions(
        string role,
        HttpStatusCode protectedStatus,
        string expectedPermission)
    {
        using var client = CreateClient();
        using var protectedResponse = await client.SendAsync(TestApplicationFactory.Request(HttpMethod.Get, "/api/protected", role));
        Assert.Equal(protectedStatus, protectedResponse.StatusCode);

        using var sessionResponse = await client.SendAsync(TestApplicationFactory.Request(HttpMethod.Get, "/api/session", role));
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var user = session.GetProperty("user");
        var permissions = user.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal([expectedPermission], permissions);
    }

    [Theory]
    [InlineData("", TestApplicationFactory.TenantId, TestApplicationFactory.ObjectId)]
    [InlineData("UnknownRole", TestApplicationFactory.TenantId, TestApplicationFactory.ObjectId)]
    [InlineData("Employee", TestApplicationFactory.OtherTenantId, TestApplicationFactory.ObjectId)]
    [InlineData("Employee", TestApplicationFactory.TenantId, "not-a-guid")]
    [InlineData("Employee", TestApplicationFactory.TenantId, "00000000-0000-0000-0000-000000000000")]
    public async Task Unassigned_or_invalid_identities_are_denied(string role, string tenantId, string objectId)
    {
        using var client = CreateClient();
        using var response = await client.SendAsync(TestApplicationFactory.Request(
            HttpMethod.Get, "/api/protected", role, tenantId, objectId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Employee", "assistant.use", true)]
    [InlineData("Employee", "hr.data.read", false)]
    [InlineData("Employee", "technical.administration", false)]
    [InlineData("HR", "assistant.use", false)]
    [InlineData("HR", "hr.data.read", true)]
    [InlineData("HR", "technical.administration", false)]
    [InlineData("TechnicalAdmin", "assistant.use", false)]
    [InlineData("TechnicalAdmin", "hr.data.read", false)]
    [InlineData("TechnicalAdmin", "technical.administration", true)]
    public async Task Every_permission_policy_enforces_its_explicit_role_mapping(
        string role,
        string permission,
        bool expected)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("tid", TestApplicationFactory.TenantId),
                new Claim("oid", TestApplicationFactory.ObjectId),
                new Claim("roles", role)
            ],
            TestApplicationFactory.Scheme,
            "name",
            "roles");
        var authorization = _factory.Services.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(identity),
            resource: null,
            IdentityPolicies.ForPermission(permission));
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task Technical_admin_does_not_gain_hr_or_employee_permissions()
    {
        using var client = CreateClient();
        using var response = await client.SendAsync(TestApplicationFactory.Request(
            HttpMethod.Get, "/api/session", "TechnicalAdmin"));
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        var permissions = session.GetProperty("user").GetProperty("permissions")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(["technical.administration"], permissions);
    }

    [Fact]
    public async Task Responses_contain_no_oidc_tokens()
    {
        using var client = CreateClient();
        using var response = await client.SendAsync(TestApplicationFactory.Request(HttpMethod.Get, "/api/session"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("access_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fictional-test-configuration", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", body, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });
}
