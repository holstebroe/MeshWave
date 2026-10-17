using Xunit;

namespace MeshWave.Synchronizer.Tests;

/// <summary>
/// Tests for the per-sender rate limiter added for the P2P protocol review's H1 (MaxConnectionsPerMinutePerIp was
/// defined but never enforced; per-sender push/pull limits bound the cost of S2's store-and-forward replication).
/// </summary>
public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_AllowsUpToTheLimit_ThenRejects()
    {
        var limiter = new RateLimiter(maxPerWindow: 3, window: TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("1.2.3.4"));
        Assert.True(limiter.TryAcquire("1.2.3.4"));
        Assert.True(limiter.TryAcquire("1.2.3.4"));
        Assert.False(limiter.TryAcquire("1.2.3.4"));
        Assert.False(limiter.TryAcquire("1.2.3.4"));
    }

    [Fact]
    public void TryAcquire_TracksEachKeyIndependently()
    {
        var limiter = new RateLimiter(maxPerWindow: 1, window: TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("alice"));
        Assert.False(limiter.TryAcquire("alice"));
        Assert.True(limiter.TryAcquire("bob"));
        Assert.False(limiter.TryAcquire("bob"));
    }

    [Fact]
    public void TryAcquire_ResetsOnceTheWindowElapses()
    {
        var limiter = new RateLimiter(maxPerWindow: 1, window: TimeSpan.FromMilliseconds(20));

        Assert.True(limiter.TryAcquire("1.2.3.4"));
        Assert.False(limiter.TryAcquire("1.2.3.4"));

        Thread.Sleep(50);

        Assert.True(limiter.TryAcquire("1.2.3.4"));
    }

    [Fact]
    public void TryAcquire_FailsOpen_ForAnEmptyKey()
    {
        var limiter = new RateLimiter(maxPerWindow: 1, window: TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire(string.Empty));
        Assert.True(limiter.TryAcquire(string.Empty));
        Assert.True(limiter.TryAcquire(string.Empty));
    }

    [Fact]
    public void Prune_RemovesBucketsOlderThanMaxAge_ButKeepsRecentOnes()
    {
        var limiter = new RateLimiter(maxPerWindow: 1, window: TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("stale"));
        Thread.Sleep(30);
        Assert.True(limiter.TryAcquire("fresh"));

        limiter.Prune(TimeSpan.FromMilliseconds(15));

        // "stale"'s bucket was dropped, so it gets a fresh window; "fresh" is still within its own window and stays capped.
        Assert.True(limiter.TryAcquire("stale"));
        Assert.False(limiter.TryAcquire("fresh"));
    }
}
