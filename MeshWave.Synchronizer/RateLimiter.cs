using System.Collections.Concurrent;

namespace MeshWave.Synchronizer;

/// <summary>
/// Fixed one-minute-window rate limiter keyed by an arbitrary string (a remote IP, or a session's authenticated
/// peer address). Used by <see cref="ManifestExchangeServer"/> to bound how often a single sender can hit a given
/// kind of request (SecurityLimits.MaxConnectionsPerMinutePerIp, MaxPushesPerMinutePerSender, MaxPullsPerMinutePerSender).
/// </summary>
internal sealed class RateLimiter
{
    private sealed class Bucket
    {
        public int Count;
        public DateTime WindowStartUtc;
    }

    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public RateLimiter(int maxPerWindow, TimeSpan window)
    {
        _maxPerWindow = maxPerWindow;
        _window = window;
    }

    /// <summary>Records one attempt for <paramref name="key"/> and returns whether it is still within the limit.</summary>
    public bool TryAcquire(string key)
    {
        // Nothing to key on (e.g. an unresolvable remote address): fail open rather than blocking everyone.
        if (string.IsNullOrEmpty(key))
            return true;

        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(key, static (_, w) => new Bucket { WindowStartUtc = w }, now);

        lock (bucket)
        {
            if (now - bucket.WindowStartUtc >= _window)
            {
                bucket.WindowStartUtc = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= _maxPerWindow)
                return false;

            bucket.Count++;
            return true;
        }
    }

    /// <summary>Drops buckets untouched for a while, so a long-running node does not accumulate one entry per distinct sender forever.</summary>
    public void Prune(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        foreach (var (key, bucket) in _buckets)
        {
            lock (bucket)
            {
                if (bucket.WindowStartUtc < cutoff)
                    _buckets.TryRemove(key, out _);
            }
        }
    }
}
