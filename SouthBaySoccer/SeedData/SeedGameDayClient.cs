using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.SeedData;

public sealed class SeedGameDayClient(SeedGameDayState state) : IGameDayClient
{
    public Task<GameDayContextDto?> GetTodayContextAsync(Guid? sessionId, bool allGames, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Seed mode runs a single game a day, so the requested session and all-games flag are ignored.
        return Task.FromResult<GameDayContextDto?>(state.GetContext());
    }

    public Task<LastGameSummaryDto?> GetLastGameSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = state.GetTeamDraft(SeedFixtures.MarinaSessionId);
        var teams = draft.Teams
            .Select(team =>
            {
                var names = draft.CheckedInPlayers.ToDictionary(p => p.Player.Id, p => p.Player.DisplayName);
                var members = team.PlayerIds
                    .Select((id, index) => new LastGameTeamMemberDto(
                        id,
                        names.GetValueOrDefault(id, "Player"),
                        id == team.CaptainId,
                        // Deterministic seed tallies: the captain and first pick carry the goals.
                        id == team.CaptainId ? 2 : index == 1 ? 1 : 0,
                        id == team.CaptainId ? 1 : 0))
                    .ToArray();
                return new LastGameTeamDto(team.TeamId, team.Name, team.CaptainName, "1W", members);
            })
            .ToArray();
        return Task.FromResult<LastGameSummaryDto?>(new LastGameSummaryDto(
            SeedFixtures.MarinaSessionId,
            "Marina Field - Wednesday pickup",
            "Bay Area Soccer",
            "Marina Field",
            "Wed Jul 22, 7:30 PM",
            new DateTime(2026, 7, 23, 2, 30, 0, DateTimeKind.Utc),
            GoingCount: 14,
            CheckedInCount: 12,
            TeamCount: teams.Length,
            ResultSummary: "Team Vic 2W · Team Ade 1W 1D",
            WaitlistCount: 5,
            Teams: teams,
            CanLockTeams: true,
            CanMatchPlayers: true,
            CanApprovePostGame: true,
            MatchId: SeedFixtures.FeaturedMatchId,
            CanRateTeammates: true));
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

    public Task<IReadOnlyList<ClaimableParticipantDto>> GetUnlinkedParticipantsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ClaimableParticipantDto>>(
        [
            new ClaimableParticipantDto(Guid.NewGuid(), "victor", IsWaitlist: true),
            new ClaimableParticipantDto(Guid.NewGuid(), "chidu", IsWaitlist: false),
        ]);
    }

    public Task<ClientCommandResult> LinkParticipantAsync(Guid participantId, Guid playerProfileId, CancellationToken cancellationToken)
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

    public Task<ClientCommandResult> UnlockTeamsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientCommandResult.Success);
    }

    public Task<SessionTeamsDto?> GetSessionTeamsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = state.GetTeamDraft(sessionId);
        var names = draft.CheckedInPlayers.ToDictionary(p => p.Player.Id, p => p.Player.DisplayName);
        var teams = draft.Teams
            .Select(team => new SessionTeamDto(
                team.TeamId,
                team.Name,
                team.CaptainName,
                false,
                team.PlayerIds
                    .Select(id => new SessionTeamMemberDto(
                        id,
                        names.TryGetValue(id, out var name) ? name : "Player",
                        id == team.CaptainId,
                        false))
                    .ToArray()))
            .ToArray();
        return Task.FromResult<SessionTeamsDto?>(new SessionTeamsDto(draft.SessionId, draft.MatchId, teams));
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
