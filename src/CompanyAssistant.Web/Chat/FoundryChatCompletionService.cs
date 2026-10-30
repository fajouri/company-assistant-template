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
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                yield break;

            var text = JsonSerializer.Deserialize<StreamingChunk>(data)?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(text))
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, text);
        }
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
        [JsonPropertyName("choices")]
        public List<StreamingChoice>? Choices { get; set; }
    }

    private sealed class StreamingChoice
    {
        [JsonPropertyName("delta")]
        public StreamingDelta? Delta { get; set; }
    }

    private sealed class StreamingDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
