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

    /// <summary>
    /// Loads the primary match and its summary facts for several sessions using bounded batch
    /// queries. Sessions without a match are omitted.
    /// </summary>
    Task<IReadOnlyList<GameDaySummaryStatsRecord>> ListGameDaySummaryStatsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchTeam>> ListMatchTeamsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamAssignment>> ListAssignmentsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<MatchEvent?> FindMatchEventAsync(Guid matchEventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchEvent>> ListMatchEventsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchResult>> ListMatchResultsAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task ReplaceMatchEventsAsync(
        Guid matchId,
        IReadOnlyList<MatchEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces only the pending rows a single player submitted for themselves, leaving every other
    /// player's rows and any already-reviewed row untouched. This makes a player's self-submission
    /// idempotent: resubmitting overwrites their own pending claim instead of stacking duplicates.
    /// </summary>
    Task ReplaceOwnPendingMatchEventsAsync(
        Guid matchId,
        Guid submittedByPlayerProfileId,
        IReadOnlyList<MatchEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the player has a participation row (Played = true) for the match, adding one if
    /// absent. Leaderboards and player stats aggregate over participation, so a self-submitter who
    /// was never drafted would otherwise have their approved goals/assists ignored.
    /// </summary>
    Task EnsurePlayerMatchParticipationAsync(
        Guid matchId,
        Guid playerProfileId,
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
        Guid? groupChatId,
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

/// <summary>Match and stat facts needed to render one completed Game Day summary.</summary>
/// <param name="SessionId">Session represented by this summary.</param>
/// <param name="Match">The session's primary match.</param>
/// <param name="Teams">Teams belonging to the primary match.</param>
/// <param name="Results">Recorded results belonging to the primary match.</param>
/// <param name="Assignments">Player-to-team assignments belonging to the primary match.</param>
/// <param name="Events">Goal and assist events belonging to the primary match.</param>
public sealed record GameDaySummaryStatsRecord(
    Guid SessionId,
    Match Match,
    IReadOnlyList<MatchTeam> Teams,
    IReadOnlyList<MatchResult> Results,
    IReadOnlyList<TeamAssignment> Assignments,
    IReadOnlyList<MatchEvent> Events);

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
    int MvpAwards,
    int Wins = 0,
    int Losses = 0);

public sealed record PlayerRecentFormReadModel(
    Guid MatchId,
    DateTime SortAtUtc,
    int TeamCount,
    int Wins,
    int Draws,
    int Losses);
