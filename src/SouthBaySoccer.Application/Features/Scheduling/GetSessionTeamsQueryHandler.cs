using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record SessionTeamsModel(
    Guid SessionId,
    Guid MatchId,
    IReadOnlyList<SessionTeamModel> Teams);

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
/// can see who they are with and who they are up against.
/// </summary>
public sealed class GetSessionTeamsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository)
{
    public async Task<SessionTeamsModel> HandleAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, sessionId, cancellationToken);
        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
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

        return new SessionTeamsModel(sessionId, match.Id, teamModels);
    }
}
