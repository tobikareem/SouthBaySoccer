using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Profiles;

namespace SouthBaySoccer.Contracts.GameDay;

public enum GameDayStatus
{
    Open,
    CheckedIn,
    Closed,
    Blocked
}

public enum StatApprovalStatus
{
    Pending,
    Approved,
    NeedsReview
}

public sealed record GameDayContextDto(
    Guid SessionId,
    Guid MatchId,
    string Title,
    string Venue,
    string DateLabel,
    string GameStartLabel,
    string CheckInWindowLabel,
    string CheckInCloseLabel,
    GameDayStatus Status,
    string StatusLabel,
    bool IsSelfCheckInAvailable,
    string PrimaryActionText,
    string? BlockReason,
    string RsvpIntentLabel,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerCheckedIn,
    int GoingCount,
    int CheckedInCount,
    int LateCount,
    bool CanAssignCaptains,
    bool CanDraftTeam,
    bool CanApprovePostGame);

public sealed record CheckedInPlayerDto(
    PlayerSummaryDto Player,
    string Detail);

public sealed record CaptainAssignmentDto(
    Guid SessionId,
    Guid MatchId,
    int CaptainCount,
    IReadOnlyList<int> AvailableCaptainCounts,
    IReadOnlyList<Guid> SelectedCaptainIds,
    IReadOnlyList<CheckedInPlayerDto> CheckedInPlayers);

public sealed record MatchTeamDto(
    Guid TeamId,
    string Name,
    Guid CaptainId,
    string CaptainName,
    IReadOnlyList<Guid> PlayerIds);

public sealed record TeamDraftDto(
    Guid SessionId,
    Guid MatchId,
    Guid TeamId,
    string TeamName,
    string CaptainName,
    bool CanPickPlayers,
    bool IsLocked,
    int TeamCount,
    IReadOnlyList<CheckedInPlayerDto> CheckedInPlayers,
    IReadOnlyList<MatchTeamDto> Teams);

public sealed record PendingStatApprovalDto(
    Guid SubmissionId,
    PlayerSummaryDto Player,
    int Goals,
    int Assists,
    StatApprovalStatus Status);

public sealed record TeamResultDto(
    Guid TeamId,
    string TeamName,
    int Wins,
    int Draws,
    int Losses);

public sealed record PostGameApprovalDto(
    Guid SessionId,
    Guid MatchId,
    bool CanApprove,
    bool IsPublished,
    bool NeedsReview,
    int TeamCount,
    IReadOnlyList<TeamResultDto> TeamResults,
    IReadOnlyList<PendingStatApprovalDto> PendingApprovals);

public sealed record TeamResultUpdateDto(
    Guid TeamId,
    int Wins,
    int Draws,
    int Losses);

public sealed record RecentFormUpdateDto(
    Guid PlayerId,
    MatchResult Result);
