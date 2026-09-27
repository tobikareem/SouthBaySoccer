namespace SouthBaySoccer.Contracts.Profiles;

public enum MatchResult
{
    Win,
    Draw,
    Loss
}

public sealed record PlayerProfileDto(
    Guid PlayerId,
    string DisplayName,
    string Subtitle,
    string Initials,
    CareerStatsDto CareerStats,
    IReadOnlyList<MatchResult> RecentForm,
    string? PendingConfirmationNote,
    string Role = "Player");

public sealed record CareerStatsDto(
    int Matches,
    int Goals,
    int Assists,
    decimal AverageRating,
    int MvpAwards,
    int Likes,
    int Wins = 0,
    int Losses = 0);
