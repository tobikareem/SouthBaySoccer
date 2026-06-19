using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedLeaderboardClient : ILeaderboardClient
{
    public Task<LeaderboardDto> GetRankingAsync(
        Guid seasonId,
        LeaderboardMetric metric,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (seasonId != SeedFixtures.Season2026Id)
        {
            throw new KeyNotFoundException($"Seed season '{seasonId}' was not found.");
        }

        return Task.FromResult(SeedFixtures.Leaderboards[metric]);
    }
}
