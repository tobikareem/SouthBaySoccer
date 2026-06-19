using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedProfileClient : IProfileClient
{
    public Task<PlayerProfileDto?> GetProfileAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PlayerProfileDto?>(
            playerId == SeedFixtures.CurrentPlayerId ? SeedFixtures.Profile : null);
    }
}
