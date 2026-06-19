using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Rosters;

namespace SouthBaySoccer.Services.Clients;

public interface IRosterClient
{
    Task<RosterDto?> GetRosterAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<ClientCommandResult> SetRsvpIntentAsync(
        Guid sessionId,
        bool isGoing,
        CancellationToken cancellationToken);
}
