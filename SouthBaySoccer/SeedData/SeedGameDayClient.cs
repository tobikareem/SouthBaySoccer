using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedGameDayClient(SeedGameDayState state) : IGameDayClient
{
    public Task<GameDayContextDto?> GetTodayContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<GameDayContextDto?>(state.GetContext());
    }

    public Task<IReadOnlyList<RecentGameDto>> GetRecentGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RecentGameDto>>(
        [
            new RecentGameDto(
                SeedFixtures.MarinaSessionId,
                SeedFixtures.FeaturedMatchId,
                "Marina Field - Wednesday pickup",
                "Marina Field",
                "Wed Jul 22, 7:30 PM",
                "Completed",
                2,
                2,
                CanEditTeams: true),
        ]);
    }

    public Task<IReadOnlyList<ClaimableSessionDto>> GetMyClaimableSessionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ClaimableSessionDto>>([]);
    }

    public Task<SessionClaimablesDto?> GetSessionClaimablesAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<SessionClaimablesDto?>(
            new SessionClaimablesDto(sessionId, "You", AlreadyOnRoster: true, []));
    }

    public Task<ClientCommandResult> ClaimParticipantAsync(Guid sessionId, Guid participantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientCommandResult.Success);
    }

    public Task<ClientCommandResult> CheckInAsync(
        Guid sessionId,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.CheckIn(sessionId));
    }

    public Task<ClientCommandResult> LateCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        string reason,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.LateCheckIn(sessionId, playerProfileId, reason, idempotencyKey));
    }

    public Task<ClientCommandResult> AdminCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.AdminCheckIn(sessionId, playerProfileId));
    }

    public Task<CaptainAssignmentDto?> GetCaptainAssignmentAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CaptainAssignmentDto?>(state.GetCaptainAssignment(sessionId));
    }

    public Task<ClientCommandResult> AssignCaptainsAsync(
        Guid sessionId,
        int captainCount,
        IReadOnlyList<Guid> captainIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.AssignCaptains(sessionId, captainCount, captainIds));
    }

    public Task<TeamDraftDto?> GetTeamDraftAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TeamDraftDto?>(state.GetTeamDraft(sessionId));
    }

    public Task<ClientCommandResult> SaveTeamPicksAsync(
        Guid sessionId,
        Guid teamId,
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SaveTeamPicks(sessionId, teamId, playerIds));
    }

    public Task<ClientCommandResult> LockTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.LockTeams(sessionId));
    }

    public Task<PostGameApprovalDto?> GetPostGameApprovalAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PostGameApprovalDto?>(state.GetPostGameApproval(sessionId));
    }

    public Task<ClientCommandResult> ApproveStatAsync(
        Guid sessionId,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.ApproveStat(submissionId));
    }

    public Task<ClientCommandResult> SaveTeamResultAsync(
        Guid sessionId,
        TeamResultUpdateDto result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.SaveTeamResult(result));
    }

    public Task<ClientCommandResult> PublishPostGameAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(state.Publish());
    }
}
