using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedLeaderboardClient : ILeaderboardClient
{
    public Task<LeaderboardDto> GetRankingAsync(
        Guid seasonId,
        LeaderboardMetric metric,
        Guid? groupChatId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Seed rankings are a fixed fixture set; the group filter is a no-op in demo mode.
        _ = groupChatId;

        if (seasonId != SeedFixtures.Season2026Id)
        {
            throw new KeyNotFoundException($"Seed season '{seasonId}' was not found.");
        }

        return Task.FromResult(SeedFixtures.Leaderboards[metric]);
    }
}
