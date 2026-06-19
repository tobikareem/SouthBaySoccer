using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Stats;

namespace SouthBaySoccer.Services.Clients;

public interface IStatsClient
{
    Task<MatchStatsDto?> GetMatchStatsAsync(Guid matchId, CancellationToken cancellationToken);

    Task<ClientCommandResult> SubmitStatsAsync(
        Guid matchId,
        int goals,
        int assists,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> ConfirmStatsAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RateableTeammateDto>> GetRateableTeammatesAsync(
        Guid matchId,
        Guid raterId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> SubmitRatingsAsync(
        Guid matchId,
        Guid raterId,
        IReadOnlyList<TeammateRatingDto> ratings,
        CancellationToken cancellationToken);
}
