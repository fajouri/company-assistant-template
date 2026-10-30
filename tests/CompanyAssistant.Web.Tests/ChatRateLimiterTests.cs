using CompanyAssistant.Web.Chat;

namespace CompanyAssistant.Web.Tests;

public sealed class ChatRateLimiterTests
{
    [Fact]
    public void Allows_up_to_limit_then_rejects_within_the_window()
    {
        var limiter = new ChatRateLimiter(requestsPerMinute: 2);
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(limiter.TryAcquire("user-a", now));
        Assert.True(limiter.TryAcquire("user-a", now.AddSeconds(10)));
        Assert.False(limiter.TryAcquire("user-a", now.AddSeconds(20)));
        Assert.True(limiter.TryAcquire("user-b", now.AddSeconds(20)));
    }

    [Fact]
    public void Expired_requests_do_not_count_against_the_limit()
    {
        var limiter = new ChatRateLimiter(requestsPerMinute: 1);
        var now = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(limiter.TryAcquire("user-a", now));
        Assert.False(limiter.TryAcquire("user-a", now.AddSeconds(30)));
        Assert.True(limiter.TryAcquire("user-a", now.AddSeconds(61)));
    }
}
