using System.Runtime.CompilerServices;
using CompanyAssistant.Web.Chat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CompanyAssistant.Web.Tests;

internal sealed class FakeChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public List<string> Deltas { get; } = new();
    public bool Fail { get; set; }
    public bool FailAfterDeltas { get; set; }
    public Func<CancellationToken, Task>? AfterDeltas { get; set; }
    public ChatHistory? LastHistory { get; private set; }
    public int StreamingCallCount { get; private set; }

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        LastHistory = chatHistory;
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>(
            [new ChatMessageContent(AuthorRole.Assistant, string.Concat(Deltas))]);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastHistory = chatHistory;
        StreamingCallCount++;
        if (Fail)
            throw new ChatUnavailableException("Forced chat failure.");

        foreach (var delta in Deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, delta);
        }
        if (AfterDeltas is not null)
            await AfterDeltas(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailAfterDeltas)
            throw new ChatUnavailableException("Forced failure after deltas.");
    }
}
