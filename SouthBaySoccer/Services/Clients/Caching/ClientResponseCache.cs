using System.Collections.Concurrent;

namespace SouthBaySoccer.Services.Clients.Caching;

/// <summary>
/// Short-lived in-memory cache for API responses, so returning to a screen inside the freshness
/// window costs no network round trip.
/// </summary>
/// <remarks>
/// Sits above <see cref="HttpClient"/> in typed-client decorators, so every request it does make
/// still flows through the correlation, authentication and exception handlers unchanged. Read paths
/// only: mutations are never served from here, and they invalidate what they affect.
/// </remarks>
public sealed class ClientResponseCache : IClientResponseCache
{
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;
    // Bumped by every Clear/Invalidate. A factory that was already running when the cache was reset
    // must not write its result back afterwards - on sign-out that would re-seed the previous
    // account's data immediately after it was dropped.
    private long generation;

    public ClientResponseCache(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = timeProvider.GetUtcNow();
        if (entries.TryGetValue(key, out var entry)
            && entry.ExpiresAtUtc > nowUtc
            && entry.Value is T cached)
        {
            return cached;
        }

        var startedAtGeneration = Interlocked.Read(ref generation);
        var value = await factory(cancellationToken);
        if (Interlocked.Read(ref generation) == startedAtGeneration)
        {
            // Note a null result is stored but never served: the type check above fails for null,
            // so a not-found answer refetches each time rather than being remembered.
            entries[key] = new Entry(value, nowUtc.Add(timeToLive));
        }

        return value;
    }

    public void Invalidate(string keyPrefix)
    {
        Interlocked.Increment(ref generation);
        foreach (var key in entries.Keys)
        {
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                entries.TryRemove(key, out _);
            }
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref generation);
        entries.Clear();
    }

    private sealed record Entry(object? Value, DateTimeOffset ExpiresAtUtc);
}
