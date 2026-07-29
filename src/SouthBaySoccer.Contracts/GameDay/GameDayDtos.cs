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
    bool CanApprovePostGame,
    DateTime? StartsAtUtc = null,
    DateTime? CheckInOpensAtUtc = null,
    DateTime? CheckInClosesAtUtc = null,
    bool CanLateCheckIn = false,
    IReadOnlyList<GameDayPlayerDto>? LateCheckInPlayers = null,
    IReadOnlyList<GameDayRosterEntryDto>? Roster = null,
    bool CanManageCheckIns = false,
    bool CanSubmitOwnStats = false,
    IReadOnlyList<GameDayGameOptionDto>? TodaysGames = null,
    bool CanViewTeams = false);

/// <summary>
/// One of today's games the player can act on. The Game Day screen shows a picker built from these
/// only when there is more than one; the <see cref="IsSelected"/> entry is the one currently loaded.
/// </summary>
public sealed record GameDayGameOptionDto(
    Guid SessionId,
    string Title,
    string Venue,
    DateTime StartsAtUtc,
    string StatusLabel,
    bool IsSelected);

public sealed record RecentGameDto(
    Guid SessionId,
    Guid MatchId,
    string Title,
    string Venue,
    string DateLabel,
    string StatusLabel,
    int TeamCount,
    int PendingApprovalCount,
    bool CanEditTeams);

public sealed record GameDayPlayerDto(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsGuest);

/// <summary>
/// One person on the Game Day roster. <see cref="PlayerProfileId"/> is null for a Pickup Pal
/// participant that was never matched to a profile — they still count and still appear, but check-in
/// and captain assignment stay unavailable until they are linked.
/// </summary>
public sealed record GameDayRosterEntryDto(
    Guid? PlayerProfileId,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    bool IsCheckedIn,
    string? PickupPalParticipantId = null)
{
    /// <summary>Whether this row still needs matching to a real player profile.</summary>
    public bool IsUnlinked => PlayerProfileId is null;
}

public sealed record CheckedInPlayerDto(
    PlayerSummaryDto Player,
    string Detail);

public sealed record CaptainAssignmentDto(
    Guid SessionId,
    Guid MatchId,
    int CaptainCount,
    IReadOnlyList<int> AvailableCaptainCounts,
    IReadOnlyList<Guid> SelectedCaptainIds,
    IReadOnlyList<CheckedInPlayerDto> CheckedInPlayers,
    bool CanLockTeams = false,
    bool IsLocked = false,
    bool CanUnlockTeams = false);

public sealed record SessionTeamsDto(
    Guid SessionId,
    Guid MatchId,
    IReadOnlyList<SessionTeamDto> Teams);

public sealed record SessionTeamDto(
    Guid TeamId,
    string Name,
    string CaptainName,
    bool IsMine,
    IReadOnlyList<SessionTeamMemberDto> Members);

public sealed record SessionTeamMemberDto(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsCaptain,
    bool IsMe);

public sealed record AssignCaptainsRequest(
    int CaptainCount,
    IReadOnlyList<Guid> CaptainPlayerProfileIds);

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
    IReadOnlyList<MatchTeamDto> Teams,
    bool CanManageAllTeams = false);

public sealed record SaveTeamPicksRequest(IReadOnlyList<Guid> PlayerProfileIds);

public sealed record PendingStatApprovalDto(
    Guid SubmissionId,
    PlayerSummaryDto Player,
    int Goals,
    int Assists,
    StatApprovalStatus Status,
    PlayerSummaryDto? AssistPlayer = null,
    string Detail = "");

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
    IReadOnlyList<PendingStatApprovalDto> PendingApprovals,
    bool CanReopenResults = false,
    string GameTitle = "",
    string DateLabel = "");

public sealed record LinkParticipantRequest(Guid PlayerProfileId);

public sealed record ClaimParticipantRequest(Guid ParticipantId);

public sealed record TeamResultUpdateDto(
    Guid TeamId,
    int Wins,
    int Draws,
    int Losses);

public sealed record SavePostGameTeamResultRequest(int Wins, int Draws, int Losses);

public sealed record GameDayMutationResponse(Guid SessionId, Guid MatchId, int AffectedCount);

public sealed record RecentFormUpdateDto(
    Guid PlayerId,
    MatchResult Result);

public sealed record ClaimableParticipantDto(Guid ParticipantId, string DisplayName, bool IsWaitlist);

public sealed record SessionClaimablesDto(
    Guid SessionId,
    string MyRegisteredName,
    bool AlreadyOnRoster,
    IReadOnlyList<ClaimableParticipantDto> Claimable);

public sealed record ClaimableSessionDto(
    Guid SessionId,
    string Title,
    string DateLabel,
    int ClaimableCount);
