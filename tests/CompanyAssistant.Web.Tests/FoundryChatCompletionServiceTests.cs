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
                    "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                    "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\" world\"}}]}\n\n" +
                    "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
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

    [Theory]
    [InlineData("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"partial\"}}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"one\"}}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\" two\"}}]}\n\n")]
    public async Task Missing_done_marker_throws_after_partial_deltas(string data)
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(data, Encoding.UTF8, "text/event-stream")
            }
        };
        var service = CreateService(handler);
        var deltas = new List<string>();
        await Assert.ThrowsAsync<ChatUnavailableException>(async () =>
        {
            await foreach (var delta in service.GetStreamingChatMessageContentsAsync(History(), null, null, CancellationToken.None))
                deltas.Add(delta.Content!);
        });
        Assert.NotEmpty(deltas);
    }

    [Fact]
    public async Task Cancellation_does_not_become_provider_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService(new StubHandler());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in service.GetStreamingChatMessageContentsAsync(History(), null, null, cts.Token)) { }
        });
    }

    [Theory]
    [InlineData("stop", true)]
    [InlineData("length", false)]
    [InlineData("content_filter", false)]
    [InlineData("tool_calls", false)]
    [InlineData("function_call", false)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task Only_explicit_normal_finish_followed_by_DONE_completes(string? reason, bool succeeds)
    {
        var data = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Answer [1]\"}}]}\n\n" +
            "data: " + JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { }, finish_reason = reason } } }) +
            "\n\ndata: [DONE]\n\n";
        var service = CreateService(new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data) }
        });
        var deltas = new List<string>();
        async Task Consume()
        {
            await foreach (var delta in service.GetStreamingChatMessageContentsAsync(History()))
                deltas.Add(delta.Content!);
        }
        if (succeeds) await Consume();
        else
        {
            var error = await Assert.ThrowsAsync<ChatUnavailableException>(Consume);
            Assert.DoesNotContain("test-key", error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(["Answer [1]"], deltas); // remains provisional on rejected completion
    }

    [Theory]
    [InlineData("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\ndata: {\"error\":{\"message\":\"private provider detail\"}}\n\ndata: [DONE]\n\n")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":1,\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[{\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"late\"}}]}\n\ndata: [DONE]\n\n")]
    public async Task Incomplete_or_ambiguous_completion_fails_closed(string data)
    {
        var service = CreateService(new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data) }
        });
        await Assert.ThrowsAsync<ChatUnavailableException>(async () =>
        {
            await foreach (var _ in service.GetStreamingChatMessageContentsAsync(History())) { }
        });
    }

    [Fact]
    public async Task Choice_is_selected_by_index_and_annotation_frames_do_not_complete_it()
    {
        var service = CreateService(new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    data: {"choices":[]}
                    data: {"choices":[{"index":1,"delta":{"content":"wrong choice"},"finish_reason":"length"},{"index":0,"delta":{"content":"right choice"}}]}
                    data: {"choices":[{"index":0,"finish_reason":"stop"}]}
                    data: {"choices":[],"usage":{}}
                    data: [DONE]

                    """)
            }
        });
        var deltas = new List<string>();
        await foreach (var delta in service.GetStreamingChatMessageContentsAsync(History()))
            deltas.Add(delta.Content!);
        Assert.Equal(["right choice"], deltas);
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
