using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Infrastructure.Caching;

/// <summary>
/// Caches the active-season list. Seasons change a handful of times a year but the list is read on
/// every Pickup Pal import pass and every leaderboard request.
/// </summary>
internal sealed class CachedSeasonRepository(
    ISeasonRepository inner,
    IMemoryCache cache,
    CacheEvictionQueue evictionQueue) : ISeasonRepository
{
    internal const string ActiveSeasonsCacheKey = "seasons:active";
    // 5 rather than 15 minutes: on a second instance a longer window means a season an admin
    // just created is invisible, which silently skips imports rather than merely serving stale data.
    private static readonly TimeSpan ActiveSeasonsTimeToLive = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<Season>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(ActiveSeasonsCacheKey, out IReadOnlyList<Season>? cached) && cached is not null)
        {
            return cached;
        }

        var seasons = await inner.ListActiveAsync(cancellationToken);
        cache.Set(ActiveSeasonsCacheKey, seasons, ActiveSeasonsTimeToLive);
        return seasons;
    }

    public Task<Season?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        inner.FindByNameAsync(name, cancellationToken);

    public Task<Season?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public async Task AddAsync(Season entity, CancellationToken cancellationToken = default)
    {
        await inner.AddAsync(entity, cancellationToken);
        evictionQueue.Enqueue(ActiveSeasonsCacheKey);
    }

    public void Update(Season entity)
    {
        inner.Update(entity);
        evictionQueue.Enqueue(ActiveSeasonsCacheKey);
    }

    public void SoftDelete(Season entity)
    {
        inner.SoftDelete(entity);
        evictionQueue.Enqueue(ActiveSeasonsCacheKey);
    }
}

/// <summary>
/// Caches the active-venue list, which backs the admin pickers and the import's venue resolution.
/// </summary>
internal sealed class CachedVenueRepository(
    IVenueRepository inner,
    IMemoryCache cache,
    CacheEvictionQueue evictionQueue) : IVenueRepository
{
    internal const string ActiveVenuesCacheKey = "venues:active";
    private static readonly TimeSpan ActiveVenuesTimeToLive = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<Venue>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(ActiveVenuesCacheKey, out IReadOnlyList<Venue>? cached) && cached is not null)
        {
            return cached;
        }

        var venues = await inner.ListActiveAsync(cancellationToken);
        cache.Set(ActiveVenuesCacheKey, venues, ActiveVenuesTimeToLive);
        return venues;
    }

    // Never cached: callers use this to decide whether to CREATE a venue. A stale miss on another
    // instance would insert a duplicate row, so this read must always hit the database.
    public Task<IReadOnlyList<Venue>> ListByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default) =>
        inner.ListByNamesAsync(names, cancellationToken);

    // Not cached: keyed by arbitrary id sets, and callers use it to resolve specific venues rather
    // than to browse, so a per-call cache would mostly miss while multiplying keys.
    public Task<IReadOnlyList<Venue>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default) =>
        inner.ListByIdsAsync(ids, cancellationToken);

    public Task<Venue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public async Task AddAsync(Venue entity, CancellationToken cancellationToken = default)
    {
        await inner.AddAsync(entity, cancellationToken);
        evictionQueue.Enqueue(ActiveVenuesCacheKey);
    }

    public void Update(Venue entity)
    {
        inner.Update(entity);
        evictionQueue.Enqueue(ActiveVenuesCacheKey);
    }

    public void SoftDelete(Venue entity)
    {
        inner.SoftDelete(entity);
        evictionQueue.Enqueue(ActiveVenuesCacheKey);
    }
}
