using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Repository for match, team, and raw stat recording workflows.</summary>
public interface IStatsRepository
{
    Task<Match> CreateMatchAsync(
        Match match,
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<PlayerMatchStats> participants,
        CancellationToken cancellationToken = default);

    Task<Match?> FindMatchAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<Match?> FindPrimaryMatchBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchTeam>> ListMatchTeamsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamAssignment>> ListAssignmentsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<MatchEvent?> FindMatchEventAsync(Guid matchEventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchEvent>> ListMatchEventsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchResult>> ListMatchResultsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task ReplaceMatchEventsAsync(
        Guid matchId,
        IReadOnlyList<MatchEvent> events,
        CancellationToken cancellationToken = default);

    Task UpsertMatchResultsAsync(
        Guid matchId,
        IReadOnlyList<MatchResult> results,
        CancellationToken cancellationToken = default);

    Task ReplaceCaptainTopologyAsync(
        Guid matchId,
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<PlayerMatchStats> participants,
        CancellationToken cancellationToken = default);

    Task ReplaceTeamAssignmentsAsync(
        Guid matchId,
        Guid matchTeamId,
        IReadOnlyList<Guid> playerProfileIds,
        CancellationToken cancellationToken = default);

    Task SubmitPeerFeedbackAsync(
        Guid matchId,
        Guid voterPlayerProfileId,
        IReadOnlyList<PlayerRatingVote> votes,
        IReadOnlyList<PlayerLike> likes,
        MatchAward? mvpAward,
        CancellationToken cancellationToken = default);

    Task AddStatCorrectionAsync(StatCorrection correction, CancellationToken cancellationToken = default);

    Task AddProfileStatReassignmentAuditAsync(ProfileStatReassignmentAudit audit, CancellationToken cancellationToken = default);

    Task<int> ReassignProfileStatsAsync(
        Guid sourcePlayerProfileId,
        Guid targetPlayerProfileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderboardReadModel>> ListSeasonLeaderboardAsync(
        Guid seasonId,
        StatLeaderboardMetric metric,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<PlayerStatSummaryReadModel?> GetPlayerStatsAsync(
        Guid playerProfileId,
        Guid? seasonId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerRecentFormReadModel>> ListPlayerRecentFormAsync(
        Guid playerProfileId,
        int matchTake,
        CancellationToken cancellationToken = default);
}

public sealed record LeaderboardReadModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    Guid? IdentityUserId,
    int Appearances,
    int Goals,
    int Assists,
    decimal AverageRating,
    int RatingVoteCount,
    int Likes,
    int MvpAwards,
    decimal Value);

public sealed record PlayerStatSummaryReadModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    Guid? IdentityUserId,
    int Appearances,
    int Goals,
    int Assists,
    decimal AverageRating,
    int RatingVoteCount,
    int Likes,
    int MvpAwards);

public sealed record PlayerRecentFormReadModel(
    Guid MatchId,
    DateTime SortAtUtc,
    int TeamCount,
    int Wins,
    int Draws,
    int Losses);
