using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Groups;

namespace SouthBaySoccer.Infrastructure.Caching;

/// <summary>
/// Memoizes the player-agnostic Pickup Pal group catalog. Every sign-in and every group picker read
/// previously made its own call to the provider, which is the dominant cost of those requests and
/// returns the same list for everyone.
/// </summary>
/// <remarks>
/// Only <see cref="GetAllGroupsAsync"/> is cached. <see cref="GetLinkedGroupsAsync"/> is keyed on a
/// Pickup Pal user id and feeds per-player link state, so it stays a live read.
/// </remarks>
internal sealed class CachedPickupPalGroupClient(
    IPickupPalGroupClient inner,
    IMemoryCache cache,
    IClock clock) : IPickupPalGroupClient
{
    private const string CatalogCacheKey = "pickuppal:groups:catalog";
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ServeStaleFor = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<PickupPalGroupChat>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow;
        var cached = cache.Get<CatalogEntry>(CatalogCacheKey);
        if (cached is not null && nowUtc - cached.FetchedAtUtc < FreshFor)
        {
            return cached.Groups;
        }

        try
        {
            var groups = await inner.GetAllGroupsAsync(cancellationToken);
            cache.Set(CatalogCacheKey, new CatalogEntry(groups, nowUtc), ServeStaleFor);
            return groups;
        }
        catch (Exception) when (cached is not null)
        {
            // Serving a catalog that is minutes stale beats failing a sign-in because the provider
            // is down: the list is player-agnostic reference data, not an authorization decision.
            return cached.Groups;
        }
    }

    public Task<IReadOnlyList<PickupPalGroupChat>> GetLinkedGroupsAsync(
        string pickupPalUserId,
        CancellationToken cancellationToken = default) =>
        inner.GetLinkedGroupsAsync(pickupPalUserId, cancellationToken);

    private sealed record CatalogEntry(IReadOnlyList<PickupPalGroupChat> Groups, DateTime FetchedAtUtc);
}
