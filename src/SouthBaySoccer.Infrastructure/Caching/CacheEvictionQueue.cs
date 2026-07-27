using Microsoft.Extensions.Caching.Memory;

namespace SouthBaySoccer.Infrastructure.Caching;

/// <summary>
/// Collects cache keys invalidated by a request and evicts them only once the work commits.
/// </summary>
/// <remarks>
/// Evicting at write time would leave a window between the write and <c>SaveChanges</c> in which a
/// concurrent read could repopulate the entry from pre-commit state, pinning stale data for a whole
/// TTL. Draining after a successful commit closes that window; a request that never commits
/// correctly evicts nothing. Scoped, so the pending set is per-request and needs no locking.
/// </remarks>
public sealed class CacheEvictionQueue(IMemoryCache cache)
{
    private readonly HashSet<string> pendingKeys = new(StringComparer.Ordinal);

    /// <summary>Marks a cache key for eviction once the current work commits.</summary>
    public void Enqueue(string cacheKey) => pendingKeys.Add(cacheKey);

    /// <summary>Evicts every key enqueued since the last flush.</summary>
    public void Flush()
    {
        foreach (var cacheKey in pendingKeys)
        {
            cache.Remove(cacheKey);
        }

        pendingKeys.Clear();
    }
}
