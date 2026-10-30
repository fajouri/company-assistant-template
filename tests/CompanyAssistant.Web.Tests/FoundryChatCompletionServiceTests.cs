using System.Net;
using System.Text;
using System.Text.Json;
using CompanyAssistant.Web.Chat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CompanyAssistant.Web.Tests;

public sealed class FoundryChatCompletionServiceTests
{
    private static ChatSettings Settings() => new(
        "https://chat.invalid", "test-key", "gpt-test", "2024-02-01",
        4, 8_000, 4, 2_000, 512, 30, 10);

    private static FoundryChatCompletionService CreateService(StubHandler handler) =>
        new(Settings(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan });

    private static ChatHistory History()
    {
        var history = new ChatHistory();
        history.AddSystemMessage("system");
        history.AddUserMessage("question");
        return history;
    }

    [Fact]
    public async Task Streams_deltas_and_sends_a_bounded_grounded_request()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
                    "data: [DONE]\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            }
        };
        var service = CreateService(handler);

        var deltas = new List<string>();
        await foreach (var content in service.GetStreamingChatMessageContentsAsync(History(), null, null, CancellationToken.None))
            deltas.Add(content.Content ?? string.Empty);

        Assert.Equal(["Hello", " world"], deltas);
        Assert.Equal("https://chat.invalid/openai/deployments/gpt-test/chat/completions?api-version=2024-02-01",
            handler.RequestUri);
        Assert.Equal("test-key", handler.ApiKey);

        var body = JsonDocument.Parse(handler.RequestBody).RootElement;
        Assert.Equal(512, body.GetProperty("max_tokens").GetInt32());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.Equal(2, body.GetProperty("messages").GetArrayLength());
        Assert.Equal("system", body.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Non_success_status_throws_without_leaking_the_error_body()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("provider secret detail", Encoding.UTF8, "text/plain")
            }
        };
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<ChatUnavailableException>(async () =>
        {
            await foreach (var _ in service.GetStreamingChatMessageContentsAsync(History(), null, null, CancellationToken.None))
            {
            }
        });

        Assert.DoesNotContain("provider secret detail", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("chat.invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfigured_settings_fail_closed()
    {
        var unconfigured = new ChatSettings("", "", "", "2024-02-01", 4, 8_000, 4, 2_000, 512, 30, 10);
        var service = new FoundryChatCompletionService(unconfigured, new HttpClient(new StubHandler()));

        var exception = await Assert.ThrowsAsync<ChatUnavailableException>(async () =>
        {
            await foreach (var _ in service.GetStreamingChatMessageContentsAsync(History(), null, null, CancellationToken.None))
            {
            }
        });

        Assert.Contains("not configured", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        public string? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("api-key", out var keys) ? keys.SingleOrDefault() : null;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
