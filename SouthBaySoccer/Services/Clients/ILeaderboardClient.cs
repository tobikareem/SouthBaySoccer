using SouthBaySoccer.Contracts.Leaderboards;

namespace SouthBaySoccer.Services.Clients;

public interface ILeaderboardClient
{
    Task<LeaderboardDto> GetRankingAsync(
        Guid seasonId,
        LeaderboardMetric metric,
        Guid? groupChatId,
        CancellationToken cancellationToken);
}
