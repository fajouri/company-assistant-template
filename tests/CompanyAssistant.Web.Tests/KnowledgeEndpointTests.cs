using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CompanyAssistant.Web.Knowledge;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyAssistant.Web.Tests;

public sealed class KnowledgeEndpointTests
{
    private static void Seed(KnowledgeTestFactory factory)
    {
        factory.RetrievalStore.Chunks.Clear();
        factory.RetrievalStore.Chunks.Add((
            FakeRetrievalStore.ChunkFor("leave", "Leave policy", "Leave body", AccessPolicies.Employee),
            AccessPolicies.Employee));
        factory.RetrievalStore.Chunks.Add((
            FakeRetrievalStore.ChunkFor("hr", "HR contacts", "HR body", AccessPolicies.Hr),
            AccessPolicies.Hr));

        factory.RetrievalStore.Sources.Clear();
        factory.RetrievalStore.Sources.Add((new SourceDocument(
            "leave", "Leave policy", "1.0", AccessPolicies.Employee,
            new DateOnly(2025, 1, 1), ApprovalStates.Approved, "leave.md", "# Leave\n\nBody.\n"), AccessPolicies.Employee));
        factory.RetrievalStore.Sources.Add((new SourceDocument(
            "hr", "HR contacts", "1.0", AccessPolicies.Hr,
            new DateOnly(2025, 1, 1), ApprovalStates.Approved, "hr.md", "# HR\n\nBody.\n"), AccessPolicies.Hr));
    }

    private static HttpClient CreateClient(KnowledgeTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static HttpRequestMessage SearchRequest(string role, string query)
    {
        var request = TestApplicationFactory.Request(HttpMethod.Post, "/api/search", role);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task Anonymous_search_is_unauthorized()
    {
        using var factory = new KnowledgeTestFactory();
        using var client = CreateClient(factory);
        using var response = await client.PostAsJsonAsync("/api/search", new { query = "leave" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Technical_admin_cannot_search()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(SearchRequest("TechnicalAdmin", "leave"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_sees_only_employee_content()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(SearchRequest("Employee", "leave"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Leave policy", body);
        Assert.DoesNotContain("HR contacts", body);
    }

    [Fact]
    public async Task HR_sees_only_hr_content()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(SearchRequest("HR", "contacts"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("HR contacts", body);
        Assert.DoesNotContain("Leave policy", body);
    }

    [Fact]
    public async Task Employee_cannot_read_the_hr_source_document()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(
            TestApplicationFactory.Request(HttpMethod.Get, "/api/documents/hr", "Employee"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HR_can_read_the_hr_source_document()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(
            TestApplicationFactory.Request(HttpMethod.Get, "/api/documents/hr", "HR"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("HR contacts", body);
        Assert.Contains("hr.md", body);
    }

    [Fact]
    public async Task Search_with_empty_query_is_bad_request()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(SearchRequest("Employee", "   "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_response_does_not_expose_raw_filesystem_paths()
    {
        using var factory = new KnowledgeTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await client.SendAsync(SearchRequest("Employee", "leave"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("C:\\", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/policies/leave.md", body, StringComparison.Ordinal);
    }
}
