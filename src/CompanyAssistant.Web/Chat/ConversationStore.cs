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

    public void AppendExchange(Conversation conversation, string question, string answer, IReadOnlyList<SourceStamp> sources)
    {
        if (sources.Count == 0 || sources.Count > settings.MaxHistorySources)
            throw new ArgumentException("A complete bounded dependency set is required.", nameof(sources));
        conversation.AppendExchange(question, answer, settings.MaxHistoryTurns, sources);
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

    public void AppendExchange(string question, string answer, int maxHistoryTurns, IReadOnlyList<SourceStamp> sources)
    {
        lock (_turns)
        {
            // Keep each question paired with the exact answer and provenance from its
            // generation, even when requests to this conversation finish concurrently.
            _turns.Add(new ConversationTurn("user", Bound(question)));
            _turns.Add(new ConversationTurn("assistant", Bound(answer), sources.ToArray()));
            var maxEntries = maxHistoryTurns * 2;
            while (_turns.Count > maxEntries)
                _turns.RemoveAt(0);
        }
    }

    private static string Bound(string text) => text.Length <= ConversationTurn.MaxTurnChars
        ? text : text[..ConversationTurn.MaxTurnChars];
}
