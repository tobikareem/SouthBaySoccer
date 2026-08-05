using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;

namespace SouthBaySoccer.Services.Clients;

public interface IGameDayClient
{
    /// <summary>
    /// Today's Game Day context. Pass a <paramref name="sessionId"/> to load a specific one of
    /// today's games (from <see cref="GameDayContextDto.TodaysGames"/>); null lets the server pick.
    /// <paramref name="allGames"/> asks for every game today — honoured only for game admins.
    /// </summary>
    Task<GameDayContextDto?> GetTodayContextAsync(Guid? sessionId, bool allGames, CancellationToken cancellationToken);

    /// <summary>The player's most recent past game, for the no-game-today state; null when none.</summary>
    Task<LastGameSummaryDto?> GetLastGameSummaryAsync(CancellationToken cancellationToken);

    /// <summary>The player's three newest attended-game summaries, newest first.</summary>
    Task<IReadOnlyList<LastGameSummaryDto>> GetRecentGameSummariesAsync(CancellationToken cancellationToken);

    /// <summary>Games already played inside the admin edit window, for game-admin follow-up.</summary>
    Task<IReadOnlyList<RecentGameDto>> GetRecentGamesAsync(CancellationToken cancellationToken);

    /// <summary>Recent games this signed-in player is not on but could claim a spot in.</summary>
    Task<IReadOnlyList<ClaimableSessionDto>> GetMyClaimableSessionsAsync(CancellationToken cancellationToken);

    /// <summary>Unclaimed entries on a session, plus the caller's registered name for context.</summary>
    Task<SessionClaimablesDto?> GetSessionClaimablesAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Claims an unclaimed participant row as the signed-in player.</summary>
    Task<ClientCommandResult> ClaimParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken);

    /// <summary>Game-admin: a session's unclaimed imported entries, to match to real profiles.</summary>
    Task<IReadOnlyList<ClaimableParticipantDto>> GetUnlinkedParticipantsAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Game-admin: link (or re-link) an imported participant to a chosen player profile.</summary>
    Task<ClientCommandResult> LinkParticipantAsync(Guid participantId, Guid playerProfileId, CancellationToken cancellationToken);

    Task<ClientCommandResult> CheckInAsync(
        Guid sessionId,
        Guid idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> LateCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        string reason,
        Guid idempotencyKey,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> AdminCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid idempotencyKey,
        CancellationToken cancellationToken);

    Task<CaptainAssignmentDto?> GetCaptainAssignmentAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> AssignCaptainsAsync(
        Guid sessionId,
        int captainCount,
        IReadOnlyList<Guid> captainIds,
        CancellationToken cancellationToken);

    Task<TeamDraftDto?> GetTeamDraftAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> SaveTeamPicksAsync(
        Guid sessionId,
        Guid teamId,
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken);

    /// <summary>One snake-draft pick by the on-the-clock captain (or an admin on their behalf).</summary>
    Task<ClientCommandResult> DraftPickAsync(
        Guid sessionId,
        Guid playerId,
        CancellationToken cancellationToken);

    /// <summary>Admin-only: re-deals every team by rating balance; the server deals the next variant each run.</summary>
    Task<ClientCommandResult> AutoBalanceTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> LockTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> UnlockTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<SessionTeamsDto?> GetSessionTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<PostGameApprovalDto?> GetPostGameApprovalAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> ApproveStatAsync(
        Guid sessionId,
        Guid submissionId,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> SaveTeamResultAsync(
        Guid sessionId,
        TeamResultUpdateDto result,
        CancellationToken cancellationToken);

    Task<ClientCommandResult> PublishPostGameAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}
