namespace CompanyAssistant.Web.Chat;

// The wire model accepted by POST /api/chat. Clients may only supply a message and an
// optional conversation id; they cannot supply deployment URLs, system prompts, model
// credentials or citations. Citations are always derived server-side from retrieved chunks.
internal sealed record ChatRequest(string? Message, string? ConversationId);

internal sealed record ChatCitation(int Index, long ChunkId, string DocumentId, string Title, string Version);

internal sealed record ConversationTurn(string Role, string Text)
{
    public const int MaxTurnChars = 2_000;
}

internal abstract record ChatEvent;

internal sealed record SourcesEvent(IReadOnlyList<ChatCitation> Citations) : ChatEvent;

internal sealed record DeltaEvent(string Text) : ChatEvent;

internal sealed record DoneEvent(bool Limited, IReadOnlyList<ChatCitation> Citations) : ChatEvent;

internal sealed class ChatUnavailableException(string message) : Exception(message);
