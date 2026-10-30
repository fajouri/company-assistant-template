namespace CompanyAssistant.Web.Chat;

// Sliding-window request limiter keyed by the authenticated object id. Bounds the rate at
// which one employee can drive (potentially expensive) model calls without any shared state.
internal sealed class ChatRateLimiter(int requestsPerMinute)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);

    public bool TryAcquire(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_recent.TryGetValue(key, out var queue))
                _recent[key] = queue = new Queue<DateTimeOffset>();

            while (queue.Count > 0 && now - queue.Peek() > Window)
                queue.Dequeue();

            if (queue.Count >= requestsPerMinute)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }
}
