using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Application.Abstractions.Caching;

namespace SouthBaySoccer.Infrastructure.Caching;

/// <summary>
/// Per-instance <see cref="IMemoryCache"/> implementation of <see cref="IReadThroughCache"/>.
/// </summary>
/// <remarks>
/// Deliberately per-instance rather than distributed: every entry has a short absolute expiry, so
/// two workers can diverge only for that window, and nothing here is authoritative. That keeps the
/// deployment free of an extra dependency and an extra failure mode.
/// </remarks>
internal sealed class MemoryReadThroughCache(IMemoryCache cache) : IReadThroughCache
{
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        // Absolute rather than sliding: a continuously popular leaderboard must still refresh on
        // schedule instead of pinning one snapshot for as long as people keep looking at it.
        cache.Set(key, value, timeToLive);
        return value;
    }
}
