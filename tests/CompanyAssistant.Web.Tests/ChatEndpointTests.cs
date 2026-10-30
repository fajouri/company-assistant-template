using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CompanyAssistant.Web.Knowledge;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyAssistant.Web.Tests;

public sealed class ChatEndpointTests
{
    private static void Seed(ChatTestFactory factory)
    {
        factory.RetrievalStore.Chunks.Clear();
        factory.RetrievalStore.Chunks.Add((
            FakeRetrievalStore.ChunkFor("leave", "Leave policy", "Leave body", AccessPolicies.Employee),
            AccessPolicies.Employee));
        factory.RetrievalStore.Chunks.Add((
            FakeRetrievalStore.ChunkFor("hr", "HR contacts", "HR body", AccessPolicies.Hr),
            AccessPolicies.Hr));
    }

    private static HttpClient CreateClient(ChatTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<string> AntiforgeryTokenAsync(
        HttpClient client,
        string role,
        string objectId = TestApplicationFactory.ObjectId)
    {
        using var response = await client.SendAsync(
            TestApplicationFactory.Request(HttpMethod.Get, "/api/auth/antiforgery", role, TestApplicationFactory.TenantId, objectId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-CompanyAssistant.XSRF=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]);
    }

    private static async Task<HttpResponseMessage> ChatAsync(
        HttpClient client,
        string role,
        string message,
        string token,
        string? conversationId = null,
        string objectId = TestApplicationFactory.ObjectId)
    {
        var request = TestApplicationFactory.Request(HttpMethod.Post, "/api/chat", role, TestApplicationFactory.TenantId, objectId);
        if (token.Length > 0)
            request.Headers.Add("X-CSRF-TOKEN", token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { message, conversationId }), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static List<JsonElement> ParseEvents(string body) =>
        body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .ToList();

    [Fact]
    public async Task Anonymous_chat_is_unauthorized()
    {
        using var factory = new ChatTestFactory();
        using var client = CreateClient(factory);
        using var response = await client.PostAsJsonAsync("/api/chat", new { message = "leave" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Technical_admin_cannot_chat()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await ChatAsync(client, "TechnicalAdmin", "leave", token: "");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Chat_without_csrf_token_is_rejected()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        using var response = await ChatAsync(client, "Employee", "leave", token: "");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Employee_receives_streamed_answer_with_server_verified_sources()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Deltas.AddRange(["You get ", "25 days [1]."]);
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var response = await ChatAsync(client, "Employee", "How much leave?", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        Assert.Contains(events, e => e.GetProperty("type").GetString() == "sources");
        Assert.Contains(events, e => e.GetProperty("type").GetString() == "delta");
        var done = events.Single(e => e.GetProperty("type").GetString() == "done");

        var sources = events.Single(e => e.GetProperty("type").GetString() == "sources")
            .GetProperty("citations").EnumerateArray().ToList();
        Assert.Single(sources);
        Assert.Equal("leave", sources[0].GetProperty("documentId").GetString());

        Assert.False(done.GetProperty("limited").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(done.GetProperty("conversationId").GetString()));
        var deltaText = string.Concat(events
            .Where(e => e.GetProperty("type").GetString() == "delta")
            .Select(e => e.GetProperty("text").GetString()));
        Assert.Equal("You get 25 days [1].", deltaText);
    }
    [Fact]
    public async Task Employee_does_not_receive_hr_citations()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Deltas.Add("HR data would leak here.");
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var response = await ChatAsync(client, "Employee", "Show me HR contacts", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        var citations = events.Single(e => e.GetProperty("type").GetString() == "sources")
            .GetProperty("citations").EnumerateArray().ToList();
        Assert.DoesNotContain(citations, c => c.GetProperty("documentId").GetString() == "hr");
    }

    [Fact]
    public async Task Conversation_is_owned_by_its_creator_and_hidden_from_other_users()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Deltas.Add("ok [1]");

        using var owner = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(owner, "Employee");
        using var created = await ChatAsync(owner, "Employee", "How much leave?", token);
        var events = ParseEvents(await created.Content.ReadAsStringAsync());
        var conversationId = events.Single(e => e.GetProperty("type").GetString() == "done")
            .GetProperty("conversationId").GetString()!;

        using var other = CreateClient(factory);
        var otherObjectId = "55555555-5555-5555-5555-555555555555";
        var otherToken = await AntiforgeryTokenAsync(other, "Employee", otherObjectId);
        using var stolen = await ChatAsync(
            other, "Employee", "follow up", otherToken, conversationId,
            objectId: otherObjectId);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    [Fact]
    public async Task Unknown_conversation_returns_not_found()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var response = await ChatAsync(client, "Employee", "hello", token, "no-such-conversation");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_returns_429_after_threshold()
    {
        using var factory = new ChatTestFactory(requestsPerMinute: 1);
        Seed(factory);
        factory.ChatCompletion.Deltas.Add("ok");
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var first = await ChatAsync(client, "Employee", "first question", token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await ChatAsync(client, "Employee", "second question", token);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Provider_failure_returns_generic_error_without_leaking_details()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Fail = true;
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var response = await ChatAsync(client, "Employee", "How much leave?", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("chat.invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-test-key", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Forced chat failure", body, StringComparison.Ordinal);

        var errorEvent = ParseEvents(body).Single(e => e.GetProperty("type").GetString() == "error");
        Assert.Equal("The assistant service is unavailable. Please try again later.",
            errorEvent.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Partial_provider_answer_is_neither_published_nor_saved()
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Deltas.Add("Partial answer [1]");
        factory.ChatCompletion.FailAfterDeltas = true;
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");
        using var response = await ChatAsync(client, "Employee", "How much leave?", token);
        var events = ParseEvents(await response.Content.ReadAsStringAsync());
        Assert.Single(events);
        Assert.Equal("error", events[0].GetProperty("type").GetString());
        Assert.Equal("provider", events[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Chat_without_configuration_returns_503()
    {
        using var factory = new ChatTestFactory(configureChat: false);
        Seed(factory);
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");

        using var response = await ChatAsync(client, "Employee", "How much leave?", token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("acl", false)]
    [InlineData("revoked", false)]
    [InlineData("deleted", false)]
    [InlineData("content", false)]
    [InlineData("version", false)]
    public async Task History_is_reused_only_while_every_source_is_current_and_authorized(string change, bool retained)
    {
        using var factory = new ChatTestFactory();
        Seed(factory);
        factory.ChatCompletion.Deltas.Add("Old answer [1].");
        using var client = CreateClient(factory);
        var token = await AntiforgeryTokenAsync(client, "Employee");
        using var first = await ChatAsync(client, "Employee", "How much leave?", token);
        var id = ParseEvents(await first.Content.ReadAsStringAsync())
            .Single(e => e.GetProperty("type").GetString() == "done")
            .GetProperty("conversationId").GetString();

        var original = factory.RetrievalStore.Chunks[0];
        if (change == "acl") factory.RetrievalStore.Chunks[0] = (original.Chunk, AccessPolicies.Hr);
        if (change == "revoked" || change == "deleted") factory.RetrievalStore.Chunks.RemoveAt(0);
        if (change == "content") factory.RetrievalStore.Chunks[0] = (original.Chunk with { Content = "New body" }, original.Policy);
        if (change == "version") factory.RetrievalStore.Chunks[0] = (original.Chunk with { Version = "2.0" }, original.Policy);
        if (change == "acl" || change == "revoked" || change == "deleted")
            factory.RetrievalStore.Chunks.Add((
                FakeRetrievalStore.ChunkFor("other", "Other", "Other body", AccessPolicies.Employee) with { ChunkId = 2 },
                AccessPolicies.Employee));

        factory.ChatCompletion.Deltas.Clear();
        factory.ChatCompletion.Deltas.Add("New answer [1].");
        using var second = await ChatAsync(client, "Employee", "And how many days?", token, id);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var prompt = factory.ChatCompletion.LastHistory;
        Assert.NotNull(prompt);
        Assert.Equal(retained, prompt.Any(item => item.Content == "Old answer [1]."));
        Assert.Equal(retained, prompt.Any(item => item.Content == "How much leave?"));
        Assert.Equal(retained, factory.Embeddings.LastTexts.Single().Contains("How much leave?", StringComparison.Ordinal));
    }
}


