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

        var value = await factory(cancellationToken);
        // A null response means "not found", which is a real answer worth remembering for the
        // window; the TryGetValue type check above simply misses and refetches if it cannot cast.
        entries[key] = new Entry(value, nowUtc.Add(timeToLive));
        return value;
    }

    public void Invalidate(string keyPrefix)
    {
        foreach (var key in entries.Keys)
        {
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                entries.TryRemove(key, out _);
            }
        }
    }

    public void Clear() => entries.Clear();

    private sealed record Entry(object? Value, DateTimeOffset ExpiresAtUtc);
}
