using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Sessions;

namespace SouthBaySoccer.Services.Clients.Caching;

/// <summary>
/// Caches the sessions dashboard, which Sessions Home and Schedule both request and which every
/// tab switch re-requested. Mutations invalidate it so a fresh RSVP is never hidden behind the TTL.
/// </summary>
internal sealed class CachedSessionsClient(
    ISessionsClient inner,
    IClientResponseCache cache) : ISessionsClient
{
    internal const string DashboardCacheKey = "sessions:dashboard";
    private static readonly TimeSpan DashboardTimeToLive = TimeSpan.FromSeconds(30);

    public Task<SessionsDashboardDto> GetDashboardAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            DashboardCacheKey,
            DashboardTimeToLive,
            inner.GetDashboardAsync,
            cancellationToken);

    // Not cached: the detail screen shows live capacity and the player's own RSVP state, which is
    // exactly what someone opening a session is checking.
    public Task<SessionDetailDto?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        inner.GetSessionAsync(sessionId, cancellationToken);

    public async Task<ClientCommandResult> JoinWaitlistAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await inner.JoinWaitlistAsync(sessionId, cancellationToken);
        // Invalidate regardless of the reported outcome: a failure here can still have been applied
        // server-side (a timeout on a committed write), and a stale feed is worse than one refetch.
        cache.Invalidate("sessions:");
        return result;
    }
}
