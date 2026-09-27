using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Services.Clients;

public interface IProfileClient
{
    Task<PlayerProfileDto?> GetProfileAsync(Guid playerId, CancellationToken cancellationToken);

    Task<PlayerProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the signed-in player's N9ja Bay account. The server revokes every session; the
    /// Pickup Pal account is left intact.
    /// </summary>
    Task DeleteCurrentAccountAsync(CancellationToken cancellationToken);
}
