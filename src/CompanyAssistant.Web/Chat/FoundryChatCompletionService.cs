using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CompanyAssistant.Web.Chat;

// Semantic Kernel chat completion service backed by a configurable Foundry/Azure-OpenAI-
// compatible chat deployment. It fails closed (throws ChatUnavailableException) when Chat
// configuration is absent, mirrors the existing FoundryEmbeddingClient transport style, and
// yields streaming text deltas from the server-sent-events response. Provider error bodies
// are never surfaced to callers.
internal sealed class FoundryChatCompletionService(ChatSettings settings, HttpClient http) : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
                builder.Append(chunk.Content);
        }

        return [new ChatMessageContent(AuthorRole.Assistant, builder.ToString())];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
        request.Headers.TryAddWithoutValidation("api-key", settings.ApiKey);
        request.Content = JsonContent.Create(BuildRequestBody(chatHistory));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ChatUnavailableException($"Chat request failed with status {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var stoppedNormally = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                if (!stoppedNormally)
                    throw new ChatUnavailableException("Chat completion was not confirmed.");
                yield break;
            }

            var chunk = JsonSerializer.Deserialize<StreamingChunk>(data)
                ?? throw new ChatUnavailableException("Invalid chat stream.");
            if (chunk.Error is not null)
                throw new ChatUnavailableException("Chat provider stream failed.");
            // Request uses the default n=1. Never infer the selected choice from array order.
            var choices = chunk.Choices?.Where(choice => choice.Index == 0).ToArray() ?? [];
            if (choices.Length == 0) continue; // e.g. Azure prompt annotations / usage frames.
            if (choices.Length != 1 || stoppedNormally)
                throw new ChatUnavailableException("Invalid chat completion state.");
            var choice = choices[0];
            if (choice.FinishReason is not null)
            {
                if (choice.FinishReason != "stop")
                    throw new ChatUnavailableException("Chat generation did not complete normally.");
                stoppedNormally = true;
            }
            var text = choice.Delta?.Content;
            if (!string.IsNullOrEmpty(text))
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, text);
        }

        throw new ChatUnavailableException("Chat stream ended before completion.");
    }

    private void EnsureConfigured()
    {
        if (!settings.IsConfigured)
            throw new ChatUnavailableException(
                "Chat is not configured. Set Chat:Endpoint, Chat:ApiKey and Chat:Deployment before answering questions.");
    }

    private ChatCompletionRequest BuildRequestBody(ChatHistory chatHistory) => new(
        chatHistory.Select(message => new ChatMessage(RoleName(message.Role), message.Content ?? string.Empty)).ToList(),
        0,
        settings.MaxOutputTokens,
        Stream: true);

    private string BuildUrl()
    {
        var endpoint = settings.Endpoint.TrimEnd('/');
        return $"{endpoint}/openai/deployments/{Uri.EscapeDataString(settings.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(settings.ApiVersion)}";
    }

    private static string RoleName(AuthorRole role) =>
        role.Equals(AuthorRole.System) ? "system"
        : role.Equals(AuthorRole.Assistant) ? "assistant"
        : "user";

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class StreamingChunk
    {
        [JsonPropertyName("error")]
        public JsonElement? Error { get; set; }

        [JsonPropertyName("choices")]
        public List<StreamingChoice>? Choices { get; set; }
    }

    private sealed class StreamingChoice
    {
        [JsonPropertyName("index")]
        public int? Index { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("delta")]
        public StreamingDelta? Delta { get; set; }
    }

    private sealed class StreamingDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
