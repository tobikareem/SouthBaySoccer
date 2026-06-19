using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedRosterClient(SeedState state) : IRosterClient
{
    public Task<RosterDto?> GetRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.GetRoster(sessionId));
    }

    public Task<ClientCommandResult> SetRsvpIntentAsync(
        Guid sessionId,
        bool isGoing,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SetRsvpIntent(sessionId, isGoing));
    }
}
