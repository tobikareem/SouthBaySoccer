using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Services.Clients;

public interface IProfileClient
{
    Task<PlayerProfileDto?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken);
}
