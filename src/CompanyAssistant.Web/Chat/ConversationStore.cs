namespace CompanyAssistant.Web.Chat;

// In-memory, single-origin conversation state. Raw conversation content is never persisted
// to the database by default: turns live only in this process-local store, are bounded per
// turn and per conversation, and are evicted oldest-first when the process-wide cap is hit.
// A conversation is owned by exactly one (tenant, object) pair and cannot be read by others.
internal sealed class ConversationStore(ChatSettings settings)
{
    private const int MaxConversations = 1_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();

    public Conversation? FindOwned(string conversationId, string tenantId, string objectId)
    {
        lock (_gate)
        {
            return _conversations.TryGetValue(conversationId, out var conversation)
                && conversation.OwnerTenantId == tenantId
                && conversation.OwnerObjectId == objectId
                ? conversation
                : null;
        }
    }

    public Conversation Create(string tenantId, string objectId)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerTenantId = tenantId,
            OwnerObjectId = objectId
        };

        lock (_gate)
        {
            _conversations[conversation.Id] = conversation;
            _insertionOrder.Enqueue(conversation.Id);
            while (_conversations.Count > MaxConversations && _insertionOrder.Count > 0)
            {
                var oldest = _insertionOrder.Dequeue();
                _conversations.Remove(oldest);
            }
        }

        return conversation;
    }

    public void Append(Conversation conversation, string role, string text, IReadOnlyList<SourceStamp>? sources = null)
    {
        conversation.Append(role, text, settings.MaxHistoryTurns, sources);
    }
}

internal sealed class Conversation
{
    public required string Id { get; init; }
    public required string OwnerTenantId { get; init; }
    public required string OwnerObjectId { get; init; }

    private readonly List<ConversationTurn> _turns = [];

    public IReadOnlyList<ConversationTurn> Turns
    {
        get
        {
            lock (_turns)
            {
                return _turns.ToArray();
            }
        }
    }

    public void Append(string role, string text, int maxHistoryTurns, IReadOnlyList<SourceStamp>? sources = null)
    {
        var bounded = text.Length <= ConversationTurn.MaxTurnChars
            ? text
            : text[..ConversationTurn.MaxTurnChars];

        lock (_turns)
        {
            _turns.Add(new ConversationTurn(role, bounded, sources));
            var maxEntries = maxHistoryTurns * 2;
            while (_turns.Count > maxEntries)
                _turns.RemoveAt(0);
        }
    }
}
