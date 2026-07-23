using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;

namespace SouthBaySoccer.Services.Clients;

public interface IGameDayClient
{
    Task<GameDayContextDto?> GetTodayContextAsync(CancellationToken cancellationToken);

    /// <summary>Games already played inside the admin edit window, for game-admin follow-up.</summary>
    Task<IReadOnlyList<RecentGameDto>> GetRecentGamesAsync(CancellationToken cancellationToken);

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

    Task<ClientCommandResult> LockTeamsAsync(
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
