using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Application.Features.Stats;

public sealed record CreateMatchCommand(
    Guid SessionId,
    int MatchNumber,
    IReadOnlyList<CreateMatchTeamInput> Teams,
    IReadOnlyList<TeamAssignmentInput> Assignments);

public sealed record CreateMatchTeamInput(
    Guid MatchTeamId,
    int TeamNumber,
    string Name,
    Guid? CaptainPlayerProfileId);

public sealed record TeamAssignmentInput(
    Guid MatchTeamId,
    Guid PlayerProfileId,
    bool Started,
    int? MinutesPlayed,
    bool PlayedGoalkeeper,
    string? Position);

public sealed record CreateMatchResultModel(
    Guid MatchId,
    Guid SessionId,
    int MatchNumber,
    string Status,
    IReadOnlyList<MatchTeamModel> Teams,
    IReadOnlyList<TeamAssignmentModel> Assignments);

public sealed record MatchTeamModel(Guid MatchTeamId, int TeamNumber, string Name, Guid? CaptainPlayerProfileId);

public sealed record TeamAssignmentModel(
    Guid MatchTeamId,
    Guid PlayerProfileId,
    bool Started,
    int? MinutesPlayed,
    bool PlayedGoalkeeper,
    string? Position);

public sealed record RecordMatchEventsCommand(Guid MatchId, IReadOnlyList<MatchEventInput> Events);

public sealed record MatchEventInput(
    Guid? PlayerProfileId,
    Guid? AssistPlayerProfileId,
    Guid? MatchTeamId,
    MatchEventType EventType,
    int Minute);

public sealed record RecordMatchResultsCommand(Guid MatchId, IReadOnlyList<MatchResultInput> Results);

public sealed record MatchResultInput(
    Guid MatchTeamId,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst);

public sealed record SubmitPeerFeedbackCommand(
    Guid MatchId,
    IReadOnlyList<PlayerRatingInput> Ratings,
    IReadOnlyList<Guid> LikedPlayerProfileIds,
    Guid? MvpPlayerProfileId);

public sealed record PlayerRatingInput(Guid RatedPlayerProfileId, int Score);

// STAT-7 / STAT-8 player-facing post-game surface: a player reports their own goals/assists and
// rates the teammates they played with. Captains and game admins confirm through STAT-9.
public sealed record GetMyMatchStatsQuery(Guid MatchId);

public sealed record MyMatchStatsModel(
    Guid MatchId,
    Guid CurrentPlayerProfileId,
    string MatchSubtitle,
    int Goals,
    int Assists,
    bool IsPendingConfirmation,
    bool CanSubmit,
    bool CanConfirmTeammates,
    IReadOnlyList<TeammateStatSubmissionModel> TeammateSubmissions);

public sealed record TeammateStatSubmissionModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    int Goals,
    int Assists,
    bool IsConfirmed);

public sealed record GetRateableTeammatesQuery(Guid MatchId);

public sealed record RateableTeammateModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    string Detail);

public sealed record SubmitMyMatchStatsCommand(Guid MatchId, int Goals, int Assists);

public sealed record ConfirmPlayerSubmissionCommand(Guid MatchId, Guid PlayerProfileId);

/// <summary>Drives the Sessions-tab "Submit your latest stats" prompt.</summary>
public sealed record GetPendingStatSubmissionQuery;

public sealed record PendingStatSubmissionModel(
    Guid MatchId,
    string Title,
    string Caption,
    bool IsPendingConfirmation);

public sealed record AddStatCorrectionCommand(
    Guid MatchId,
    Guid? PlayerProfileId,
    string Reason,
    string BeforeJson,
    string AfterJson);

public sealed record ReviewMatchEventCommand(Guid MatchId, Guid MatchEventId, bool Approved, string? Note);

public sealed record ResolveMatchReviewCommand(Guid MatchId, string ResolutionNote, string BeforeJson, string AfterJson);

public sealed record StatMutationResult(Guid MatchId, int AffectedCount);

public sealed record ReassignProfileStatsCommand(Guid SourceGuestPlayerProfileId, Guid TargetPlayerProfileId);
public sealed record LockMatchStatsCommand(Guid MatchId);



public sealed record GetSeasonLeaderboardQuery(
    Guid? SeasonId,
    StatLeaderboardMetric Metric,
    int Page,
    int PageSize);

public sealed record LeaderboardModel(
    Guid SeasonId,
    string SeasonLabel,
    StatLeaderboardMetric Metric,
    string Note,
    IReadOnlyList<LeaderboardRowModel> Rows);

public sealed record LeaderboardRowModel(
    int Rank,
    PlayerSummaryModel Player,
    int Appearances,
    decimal Value);

public sealed record PlayerSummaryModel(
    Guid Id,
    string DisplayName,
    string Initials,
    string Position,
    bool IsGuest,
    Guid? IdentityId);

public sealed record GetPlayerStatsQuery(Guid PlayerProfileId, Guid? SeasonId);

public sealed record GetMyPlayerStatsQuery(Guid? SeasonId);

public sealed record PlayerStatsModel(
    Guid PlayerProfileId,
    Guid? SeasonId,
    int Matches,
    int Goals,
    int Assists,
    decimal AverageRating,
    int MvpAwards,
    int Likes);

public sealed record GetPlayerRecentFormQuery(Guid PlayerProfileId, int Take);

public sealed record RecentFormModel(Guid PlayerProfileId, IReadOnlyList<RecentFormOutcome> Results);

public enum RecentFormOutcome
{
    Win,
    Draw,
    Loss
}
