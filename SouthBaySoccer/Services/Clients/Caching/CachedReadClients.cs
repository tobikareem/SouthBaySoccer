using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Services.Clients.Caching;

/// <summary>
/// Caches the signed-in player's own profile, which every Sessions Home load reads for the greeting.
/// </summary>
/// <remarks>
/// This is per-player data held in a process-wide cache, which is only safe because
/// <see cref="IClientResponseCache.Clear"/> runs on sign-in and sign-out.
/// </remarks>
internal sealed class CachedProfileClient(IProfileClient inner, IClientResponseCache cache) : IProfileClient
{
    internal const string CurrentProfileCacheKey = "profile:me";
    private static readonly TimeSpan CurrentProfileTimeToLive = TimeSpan.FromMinutes(5);

    public Task<PlayerProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            CurrentProfileCacheKey,
            CurrentProfileTimeToLive,
            inner.GetCurrentProfileAsync,
            cancellationToken);

    // Not cached: another player's profile is opened deliberately from the directory, and the
    // per-player key space would grow with everyone the user ever viewed.
    public Task<PlayerProfileDto?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken) =>
        inner.GetProfileAsync(playerId, cancellationToken);
}

/// <summary>
/// Caches group membership, which Sessions Home and the post-sign-in gate both read.
/// </summary>
internal sealed class CachedGroupsClient(IGroupsClient inner, IClientResponseCache cache) : IGroupsClient
{
    internal const string MyGroupsCacheKey = "groups:me";
    internal const string AvailableGroupsCacheKey = "groups:available";
    private static readonly TimeSpan GroupsTimeToLive = TimeSpan.FromMinutes(5);

    public Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(MyGroupsCacheKey, GroupsTimeToLive, inner.GetMyGroupsAsync, cancellationToken);

    public Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            AvailableGroupsCacheKey,
            GroupsTimeToLive,
            inner.GetAvailableGroupsAsync,
            cancellationToken);

    public async Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        var result = await inner.LinkAsync(groupExternalId, cancellationToken);
        // Linking changes exactly what these two reads report, so neither may survive it.
        cache.Invalidate("groups:");
        return result;
    }
}

/// <summary>
/// Caches the player directory, which is identical for everyone and tolerates a minute of lag.
/// </summary>
internal sealed class CachedPlayersClient(IPlayersClient inner, IClientResponseCache cache) : IPlayersClient
{
    internal const string DirectoryCacheKey = "players:directory";
    private static readonly TimeSpan DirectoryTimeToLive = TimeSpan.FromSeconds(60);

    public Task<PlayerDirectoryDto> GetDirectoryAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(DirectoryCacheKey, DirectoryTimeToLive, inner.GetDirectoryAsync, cancellationToken);
}

/// <summary>
/// Invalidates the cached sessions dashboard when an RSVP changes.
/// </summary>
/// <remarks>
/// The dashboard payload carries IsGoing, GoingCount, WaitlistCount and IsFull, so an RSVP made on
/// the detail screen changes what a cached feed reports. Roster reads themselves are never cached —
/// live capacity is exactly what the detail screen exists to show.
/// </remarks>
internal sealed class CachedRosterClient(IRosterClient inner, IClientResponseCache cache) : IRosterClient
{
    public Task<RosterDto?> GetRosterAsync(Guid sessionId, CancellationToken cancellationToken) =>
        inner.GetRosterAsync(sessionId, cancellationToken);

    public async Task<ClientCommandResult> SetRsvpIntentAsync(
        Guid sessionId,
        bool isGoing,
        CancellationToken cancellationToken)
    {
        var result = await inner.SetRsvpIntentAsync(sessionId, isGoing, cancellationToken);
        // Invalidate regardless of reported outcome: a timeout on a committed write would otherwise
        // leave the feed showing the pre-RSVP state.
        cache.Invalidate("sessions:");
        return result;
    }
}
