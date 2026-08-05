using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record SessionTeamsModel(
    Guid SessionId,
    Guid MatchId,
    IReadOnlyList<SessionTeamModel> Teams,
    bool IsDraftInProgress = false,
    string OnTheClockLabel = "",
    IReadOnlyList<SessionTeamMemberModel>? AvailablePlayers = null,
    long DraftRevision = 0,
    string DraftValidator = "");

public sealed record SessionTeamModel(
    Guid TeamId,
    string Name,
    string CaptainName,
    bool IsMine,
    IReadOnlyList<SessionTeamMemberModel> Members);

public sealed record SessionTeamMemberModel(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsCaptain,
    bool IsMe);

/// <summary>
/// Read-only view of the teams for a session, available to any player on the roster (not just
/// captains/admins). Shows every team with its members, the caller's own team marked, so a player
/// can see who they are with and who they are up against. While the draft is still running it also
/// reports whose turn it is and who is yet to be picked, so a rostered player can watch the draft
/// unfold without being able to touch it.
/// </summary>
public sealed class GetSessionTeamsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository)
{
    public async Task<SessionTeamsModel> HandleAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await HandleConditionalAsync(sessionId, null, cancellationToken)
            ?? throw new InvalidOperationException("An unconditional teams query cannot be not modified.");

    public async Task<SessionTeamsModel?> HandleConditionalAsync(
        Guid sessionId,
        string? knownDraftValidator,
        CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, sessionId, cancellationToken);
        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            sessionId,
            cancellationToken);
        if (!roster.Any(member => member.PlayerProfileId == actor.Id)
            && !GameDayWorkflowAuthorization.IsGameAdmin(currentUser))
        {
            throw new ApplicationForbiddenException("Only players on this session's roster can view the teams.");
        }

        var match = await statsRepository.FindPrimaryMatchBySessionAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Teams have not been set for this session yet.");
        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        var assignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        var names = roster.ToDictionary(member => member.PlayerProfileId, member => member.DisplayName);

        var teamModels = teams
            .OrderBy(team => team.TeamNumber)
            .Select(team =>
            {
                var memberIds = assignments
                    .Where(assignment => assignment.MatchTeamId == team.Id)
                    .Select(assignment => assignment.PlayerProfileId)
                    .ToArray();
                var members = memberIds
                    .Select(id => new SessionTeamMemberModel(
                        id,
                        names.TryGetValue(id, out var name) ? name : "Player",
                        team.CaptainPlayerProfileId == id,
                        id == actor.Id))
                    .OrderByDescending(member => member.IsCaptain)
                    .ThenBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new SessionTeamModel(
                    team.Id,
                    team.Name,
                    team.CaptainPlayerProfileId is { } captainId && names.TryGetValue(captainId, out var captainName)
                        ? captainName
                        : "Captain",
                    memberIds.Contains(actor.Id),
                    members);
            })
            .ToArray();

        // Mid-draft, the sheets are still forming: label the state explicitly (whose turn it is)
        // and list who is yet to be picked, so a partial view reads as "in progress", never stale.
        var isDraftInProgress = match.Status == MatchStatus.Draft && teams.Count > 0;
        var onTheClockLabel = string.Empty;
        IReadOnlyList<SessionTeamMemberModel>? availablePlayers = null;
        if (isDraftInProgress)
        {
            var teamsByRank = teams.OrderBy(team => team.TeamNumber).ToArray();
            var caps = GameDayWorkflowQueries.ComputeTeamCaps(roster.Count, teamsByRank.Length);
            var nonCaptainCounts = teamsByRank
                .Select(team => assignments.Count(assignment => assignment.MatchTeamId == team.Id
                    && assignment.PlayerProfileId != team.CaptainPlayerProfileId))
                .ToArray();
            var (onTheClockTeamId, _) = GameDayWorkflowQueries.ResolveDraftTurn(teamsByRank, caps, nonCaptainCounts);
            onTheClockLabel = onTheClockTeamId is { } clockTeamId
                ? $"On the clock: {teamsByRank.Single(team => team.Id == clockTeamId).Name}"
                : "Draft complete — waiting for the lock";

            var assignedIds = assignments.Select(assignment => assignment.PlayerProfileId).ToHashSet();
            availablePlayers = roster
                .Where(member => !assignedIds.Contains(member.PlayerProfileId))
                .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(member => new SessionTeamMemberModel(
                    member.PlayerProfileId,
                    member.DisplayName,
                    IsCaptain: false,
                    member.PlayerProfileId == actor.Id))
                .ToArray();
        }

        var model = new SessionTeamsModel(
            sessionId,
            match.Id,
            teamModels,
            isDraftInProgress,
            onTheClockLabel,
            availablePlayers,
            match.DraftRevision);
        var draftValidator = GameDayWorkflowQueries.BuildDraftValidator(model.DraftRevision, model);
        return string.Equals(knownDraftValidator, draftValidator, StringComparison.Ordinal)
            ? null
            : model with { DraftValidator = draftValidator };
    }
}
