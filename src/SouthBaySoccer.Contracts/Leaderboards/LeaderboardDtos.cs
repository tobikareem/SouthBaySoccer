using SouthBaySoccer.Contracts.Players;

namespace SouthBaySoccer.Contracts.Leaderboards;

public enum LeaderboardMetric
{
    Goals,
    Assists,
    Rating,
    Mvp
}

public sealed record LeaderboardDto(
    Guid SeasonId,
    string SeasonLabel,
    LeaderboardMetric Metric,
    string Note,
    IReadOnlyList<LeaderboardRowDto> Rows);

public sealed record LeaderboardRowDto(
    int Rank,
    PlayerSummaryDto Player,
    int Appearances,
    decimal Value);
