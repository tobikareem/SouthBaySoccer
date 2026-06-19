using SouthBaySoccer.Contracts.Players;

namespace SouthBaySoccer.Contracts.Stats;

public sealed record MatchStatsDto(
    Guid MatchId,
    Guid CurrentPlayerId,
    string MatchSubtitle,
    int Goals,
    int Assists,
    bool IsPendingConfirmation,
    IReadOnlyList<TeammateStatSubmissionDto> TeammateSubmissions);

public sealed record TeammateStatSubmissionDto(
    PlayerSummaryDto Player,
    int Goals,
    int Assists,
    bool IsConfirmed);

public sealed record RateableTeammateDto(
    PlayerSummaryDto Player,
    string Detail,
    int Rating,
    bool IsLiked,
    bool IsMvp);

public sealed record TeammateRatingDto(
    Guid PlayerId,
    int Rating,
    bool IsLiked,
    bool IsMvp);
