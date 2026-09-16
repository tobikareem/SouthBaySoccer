using System.Collections.Concurrent;
using SouthBaySoccer.Application.Abstractions.Time;

namespace SouthBaySoccer.Functions.Pipeline.RateLimiting;

/// <summary>
/// Process-local sliding-window limiter. Each Functions instance keeps its own counters, so under
/// scale-out the effective limit is per instance; that is accepted for the anonymous onboarding
/// endpoints because the Pickup Pal tokens they guard are themselves single-use and 15 minutes.
/// Swap the registration for a distributed store when a shared limit is needed.
/// </summary>
public sealed class InMemorySlidingWindowRateLimiter(IClock clock) : IAnonymousRateLimiter
{
    /// <summary>Stale buckets are swept once this many calls have been made since the last sweep.</summary>
    private const int SweepInterval = 1000;

    private readonly ConcurrentDictionary<string, Bucket> buckets = new(StringComparer.Ordinal);
    private int callsSinceSweep;

    public void EnsureAllowed(RateLimitRule rule, string key)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var now = clock.UtcNow;
        var bucket = buckets.GetOrAdd($"{rule.Scope}:{key}", _ => new Bucket(rule.Window));

        lock (bucket.Hits)
        {
            var windowStart = now - rule.Window;
            while (bucket.Hits.Count > 0 && bucket.Hits.Peek() <= windowStart)
            {
                bucket.Hits.Dequeue();
            }

            if (bucket.Hits.Count >= rule.Limit)
            {
                throw new RateLimitExceededException();
            }

            bucket.Hits.Enqueue(now);
            bucket.LastHitUtc = now;
        }

        if (Interlocked.Increment(ref callsSinceSweep) >= SweepInterval)
        {
            Interlocked.Exchange(ref callsSinceSweep, 0);
            Sweep(now);
        }
    }

    private void Sweep(DateTime now)
    {
        foreach (var pair in buckets)
        {
            if (pair.Value.LastHitUtc <= now - pair.Value.Window)
            {
                buckets.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Bucket(TimeSpan window)
    {
        public TimeSpan Window { get; } = window;

        public Queue<DateTime> Hits { get; } = new();

        public DateTime LastHitUtc { get; set; }
    }
}
