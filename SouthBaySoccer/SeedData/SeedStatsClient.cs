using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedStatsClient(SeedState state) : IStatsClient
{
    public Task<MatchStatsDto?> GetMatchStatsAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<MatchStatsDto?>(
            matchId == SeedFixtures.FeaturedMatchId ? state.GetMatchStats() : null);
    }

    public Task<ClientCommandResult> SubmitStatsAsync(
        Guid matchId,
        int goals,
        int assists,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SubmitStats(matchId, goals, assists));
    }

    public Task<ClientCommandResult> ConfirmStatsAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.ConfirmStats(matchId, playerId));
    }

    public Task<IReadOnlyList<RateableTeammateDto>> GetRateableTeammatesAsync(
        Guid matchId,
        Guid raterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RateableTeammateDto>>(
            matchId == SeedFixtures.FeaturedMatchId
                ? state.GetRateableTeammates(raterId)
                : Array.Empty<RateableTeammateDto>());
    }

    public Task<ClientCommandResult> SubmitRatingsAsync(
        Guid matchId,
        Guid raterId,
        IReadOnlyList<TeammateRatingDto> ratings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SubmitRatings(matchId, raterId, ratings));
    }
}
