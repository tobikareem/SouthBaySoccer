using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Contracts.Players;
using ProfileMatchResult = SouthBaySoccer.Contracts.Profiles.MatchResult;

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
public sealed record CreateMatchRequest(
    Guid SessionId,
    int MatchNumber,
    IReadOnlyList<CreateMatchTeamRequest> Teams,
    IReadOnlyList<TeamAssignmentRequest> Assignments);

public sealed record CreateMatchTeamRequest(
    Guid MatchTeamId,
    int TeamNumber,
    string Name,
    Guid? CaptainPlayerProfileId);

public sealed record TeamAssignmentRequest(
    Guid MatchTeamId,
    Guid PlayerProfileId,
    bool Started,
    int? MinutesPlayed,
    bool PlayedGoalkeeper,
    string? Position);

public sealed record MatchResponse(
    Guid MatchId,
    Guid SessionId,
    int MatchNumber,
    string Status,
    IReadOnlyList<MatchTeamResponse> Teams,
    IReadOnlyList<TeamAssignmentResponse> Assignments);

public sealed record MatchTeamResponse(Guid MatchTeamId, int TeamNumber, string Name, Guid? CaptainPlayerProfileId);

public sealed record TeamAssignmentResponse(
    Guid MatchTeamId,
    Guid PlayerProfileId,
    bool Started,
    int? MinutesPlayed,
    bool PlayedGoalkeeper,
    string? Position);

public sealed record RecordMatchEventsRequest(IReadOnlyList<MatchEventRequest> Events);

public sealed record MatchEventRequest(
    Guid? PlayerProfileId,
    Guid? AssistPlayerProfileId,
    Guid? MatchTeamId,
    string EventType,
    int Minute);

public sealed record RecordMatchResultsRequest(IReadOnlyList<MatchResultRequest> Results);

public sealed record MatchResultRequest(
    Guid MatchTeamId,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst);

public sealed record SubmitPeerFeedbackRequest(
    IReadOnlyList<PlayerRatingRequest> Ratings,
    IReadOnlyList<Guid> LikedPlayerProfileIds,
    Guid? MvpPlayerProfileId);

public sealed record PlayerRatingRequest(Guid RatedPlayerProfileId, int Score);

public sealed record ReviewMatchEventRequest(bool Approved, string? Note);

public sealed record ResolveMatchReviewRequest(string ResolutionNote, string BeforeJson, string AfterJson);

public sealed record AddStatCorrectionRequest(
    Guid? PlayerProfileId,
    string Reason,
    string BeforeJson,
    string AfterJson);

public sealed record ReassignProfileStatsRequest(Guid SourceGuestPlayerProfileId, Guid TargetPlayerProfileId);

public sealed record StatMutationResponse(Guid MatchId, int AffectedCount);

public sealed record PlayerStatsResponse(
    Guid PlayerProfileId,
    Guid? SeasonId,
    CareerStatsDto CareerStats);

public sealed record PlayerRecentFormResponse(
    Guid PlayerProfileId,
    IReadOnlyList<ProfileMatchResult> Results);
