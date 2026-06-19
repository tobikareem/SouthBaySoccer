using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedSessionsClient(SeedState state) : ISessionsClient
{
    public Task<SessionsDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.GetDashboard());
    }

    public Task<SessionDetailDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.GetSession(sessionId));
    }

    public Task<ClientCommandResult> JoinWaitlistAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.JoinWaitlist(sessionId));
    }
}
