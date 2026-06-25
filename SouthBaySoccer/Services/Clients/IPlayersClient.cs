using SouthBaySoccer.Contracts.Players;

namespace SouthBaySoccer.Services.Clients;

public interface IPlayersClient
{
    Task<PlayerDirectoryDto> GetDirectoryAsync(CancellationToken cancellationToken);
}
