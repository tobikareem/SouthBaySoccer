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
/// Caches group membership reads, which Sessions Home, the post-sign-in gate, Profile, and the
/// membership screens all share. Every membership write invalidates the whole "groups:" prefix
/// because each one changes what at least two of these reads report.
/// </summary>
internal sealed class CachedGroupsClient(IGroupsClient inner, IClientResponseCache cache) : IGroupsClient
{
    internal const string CacheKeyPrefix = "groups:";
    internal const string MyGroupsCacheKey = "groups:me";
    internal const string CatalogCacheKey = "groups:catalog";
    internal const string MyMembershipsCacheKey = "groups:memberships";
    private static readonly TimeSpan GroupsTimeToLive = TimeSpan.FromMinutes(5);

    internal static string MembersCacheKey(Guid groupChatId) => $"groups:members:{groupChatId:D}";

    public Task<MyGroupsResponse> GetMyGroupsAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(MyGroupsCacheKey, GroupsTimeToLive, inner.GetMyGroupsAsync, cancellationToken);

    // Legacy shape served from the cached catalogue so the two reads never issue separate GETs.
    public async Task<IReadOnlyList<GroupChatDto>> GetAvailableGroupsAsync(CancellationToken cancellationToken) =>
        ApiGroupsClient.ToLegacyGroups(await GetCatalogAsync(cancellationToken));

    public async Task<MyGroupsResponse> LinkAsync(string groupExternalId, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.LinkAsync(groupExternalId, cancellationToken);
        }
        finally
        {
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public Task<IReadOnlyList<GroupWithMembershipDto>> GetCatalogAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(CatalogCacheKey, GroupsTimeToLive, inner.GetCatalogAsync, cancellationToken);

    public Task<MyGroupMembershipsResponse> GetMyMembershipsAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(MyMembershipsCacheKey, GroupsTimeToLive, inner.GetMyMembershipsAsync, cancellationToken);

    public async Task<MyGroupMembershipsResponse> RequestMembershipsAsync(
        IReadOnlyList<Guid> groupChatIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.RequestMembershipsAsync(groupChatIds, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public async Task LeaveAsync(Guid groupChatId, CancellationToken cancellationToken)
    {
        try
        {
            await inner.LeaveAsync(groupChatId, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public Task<GroupMembersResponse> GetMembersAsync(Guid groupChatId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            MembersCacheKey(groupChatId),
            GroupsTimeToLive,
            token => inner.GetMembersAsync(groupChatId, token),
            cancellationToken);

    public async Task ApproveAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        try
        {
            await inner.ApproveAsync(groupChatId, playerProfileId, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public async Task DeclineAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        try
        {
            await inner.DeclineAsync(groupChatId, playerProfileId, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public async Task RemoveMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        try
        {
            await inner.RemoveMemberAsync(groupChatId, playerProfileId, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public async Task AddMemberAsync(Guid groupChatId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        try
        {
            await inner.AddMemberAsync(groupChatId, playerProfileId, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    public async Task SetAdminAsync(Guid groupChatId, Guid playerProfileId, bool isAdmin, CancellationToken cancellationToken)
    {
        try
        {
            await inner.SetAdminAsync(groupChatId, playerProfileId, isAdmin, cancellationToken);
        }
        finally
        {
            // Invalidate regardless of outcome: a timed-out write may still have been applied.
            cache.Invalidate(CacheKeyPrefix);
        }
    }

    // Not cached: each keystroke is a distinct query and the results must reflect the latest roster.
    public Task<IReadOnlyList<PlayerSearchResultDto>> SearchPlayersAsync(string query, CancellationToken cancellationToken) =>
        inner.SearchPlayersAsync(query, cancellationToken);
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
