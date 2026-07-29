using System.Text.Json;
using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record GameDayRosterPlayerModel(
    Guid Id,
    string DisplayName,
    string Initials,
    string Position,
    bool IsGuest);

public sealed record CheckedInGameDayPlayerModel(GameDayRosterPlayerModel Player, string Detail);

public sealed record CaptainAssignmentModel(
    Guid SessionId,
    Guid MatchId,
    int CaptainCount,
    IReadOnlyList<int> AvailableCaptainCounts,
    IReadOnlyList<Guid> SelectedCaptainIds,
    IReadOnlyList<CheckedInGameDayPlayerModel> CheckedInPlayers,
    bool CanLockTeams,
    bool IsLocked,
    bool CanUnlockTeams = false);

public sealed record GameDayMatchTeamModel(
    Guid TeamId,
    string Name,
    Guid CaptainId,
    string CaptainName,
    IReadOnlyList<Guid> PlayerIds);

public sealed record TeamDraftModel(
    Guid SessionId,
    Guid MatchId,
    Guid TeamId,
    string TeamName,
    string CaptainName,
    bool CanPickPlayers,
    bool IsLocked,
    int TeamCount,
    IReadOnlyList<CheckedInGameDayPlayerModel> CheckedInPlayers,
    IReadOnlyList<GameDayMatchTeamModel> Teams,
    bool CanManageAllTeams);

public sealed record PendingStatApprovalModel(
    Guid SubmissionId,
    GameDayRosterPlayerModel Player,
    int Goals,
    int Assists,
    string Status,
    GameDayRosterPlayerModel? AssistPlayer,
    string Detail);

public sealed record GameDayTeamResultModel(
    Guid TeamId,
    string TeamName,
    int Wins,
    int Draws,
    int Losses);

public sealed record PostGameApprovalModel(
    Guid SessionId,
    Guid MatchId,
    bool CanApprove,
    bool IsPublished,
    bool NeedsReview,
    int TeamCount,
    IReadOnlyList<GameDayTeamResultModel> TeamResults,
    IReadOnlyList<PendingStatApprovalModel> PendingApprovals,
    bool CanReopenResults = false,
    string GameTitle = "",
    DateTime StartsAtUtc = default);

public sealed record ReopenPostGameResultsCommand(Guid SessionId);

public sealed record LinkParticipantToProfileCommand(Guid ParticipantId, Guid PlayerProfileId);

public sealed record AssignSessionCaptainsCommand(
    Guid SessionId,
    int CaptainCount,
    IReadOnlyList<Guid> CaptainPlayerProfileIds);

public sealed record SaveCaptainTeamPicksCommand(
    Guid SessionId,
    Guid MatchTeamId,
    IReadOnlyList<Guid> PlayerProfileIds);

public sealed record LockSessionTeamsCommand(Guid SessionId);

public sealed record UnlockSessionTeamsCommand(Guid SessionId);

public sealed record ApprovePostGameStatCommand(Guid SessionId, Guid MatchEventId);

public sealed record SavePostGameTeamResultCommand(
    Guid SessionId,
    Guid MatchTeamId,
    int Wins,
    int Draws,
    int Losses);

public sealed record PublishPostGameCommand(Guid SessionId);

public sealed record GameDayMutationModel(Guid SessionId, Guid MatchId, int AffectedCount);

public sealed class AssignSessionCaptainsCommandValidator : AbstractValidator<AssignSessionCaptainsCommand>
{
    public AssignSessionCaptainsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.CaptainCount).InclusiveBetween(2, 4);
        RuleFor(x => x).Must(x => x.CaptainPlayerProfileIds is { } ids && ids.Count == x.CaptainCount)
            .WithMessage("Captain selections must match the requested captain count.");
        RuleFor(x => x.CaptainPlayerProfileIds).Cascade(CascadeMode.Stop).NotNull().Must(HaveUniqueNonEmptyValues)
            .WithMessage("Captain selections must be distinct player ids.");
    }

    private static bool HaveUniqueNonEmptyValues(IReadOnlyList<Guid> values) =>
        values.All(x => x != Guid.Empty) && values.Distinct().Count() == values.Count;
}

public sealed class SaveCaptainTeamPicksCommandValidator : AbstractValidator<SaveCaptainTeamPicksCommand>
{
    public SaveCaptainTeamPicksCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.MatchTeamId).NotEmpty();
        RuleFor(x => x.PlayerProfileIds).Cascade(CascadeMode.Stop).NotNull().Must(HaveUniqueNonEmptyValues)
            .WithMessage("Team picks must be distinct player ids.");
    }

    private static bool HaveUniqueNonEmptyValues(IReadOnlyList<Guid> values) =>
        values.All(x => x != Guid.Empty) && values.Distinct().Count() == values.Count;
}

public sealed class LockSessionTeamsCommandValidator : AbstractValidator<LockSessionTeamsCommand>
{
    public LockSessionTeamsCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}

public sealed class UnlockSessionTeamsCommandValidator : AbstractValidator<UnlockSessionTeamsCommand>
{
    public UnlockSessionTeamsCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}

public sealed class ApprovePostGameStatCommandValidator : AbstractValidator<ApprovePostGameStatCommand>
{
    public ApprovePostGameStatCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.MatchEventId).NotEmpty();
    }
}

public sealed class SavePostGameTeamResultCommandValidator : AbstractValidator<SavePostGameTeamResultCommand>
{
    public SavePostGameTeamResultCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.MatchTeamId).NotEmpty();
        RuleFor(x => x.Wins).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Draws).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Losses).GreaterThanOrEqualTo(0);
    }
}

public sealed class PublishPostGameCommandValidator : AbstractValidator<PublishPostGameCommand>
{
    public PublishPostGameCommandValidator() => RuleFor(x => x.SessionId).NotEmpty();
}

public sealed class GetCaptainAssignmentQueryHandler(
    ICurrentUser currentUser,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository)
{
    public async Task<CaptainAssignmentModel> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        _ = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, sessionId, cancellationToken);
        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            sessionId,
            cancellationToken);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(sessionId, cancellationToken);
        var teams = match is null
            ? []
            : await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        var assignments = match is null
            ? []
            : await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        var selectedCaptainIds = teams
            .OrderBy(x => x.TeamNumber)
            .Where(x => x.CaptainPlayerProfileId.HasValue)
            .Select(x => x.CaptainPlayerProfileId!.Value)
            .ToArray();

        // An admin can unlock (revert InProgress -> Draft) as long as no results or stats have been
        // recorded; once they have, changes go through the post-game reopen flow instead.
        var canUnlockTeams = false;
        if (match?.Status == MatchStatus.InProgress)
        {
            canUnlockTeams = (await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken)).Count == 0
                && (await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken)).Count == 0;
        }

        return new CaptainAssignmentModel(
            sessionId,
            match?.Id ?? Guid.Empty,
            teams.Count is >= 2 and <= 4 ? teams.Count : 2,
            [2, 3, 4],
            selectedCaptainIds,
            GameDayWorkflowQueries.ToRosterModels(roster),
            match?.Status == MatchStatus.Draft
                && teams.Count is >= 2 and <= 4
                && teams.All(team => team.CaptainPlayerProfileId is { } captainId
                    && assignments.Any(assignment => assignment.MatchTeamId == team.Id
                        && assignment.PlayerProfileId == captainId)),
            match is not null && match.Status != MatchStatus.Draft,
            canUnlockTeams);
    }
}

public sealed class AssignSessionCaptainsCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<AssignSessionCaptainsCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly string[] FallbackTeamNames = ["Team Green", "Team White", "Team Spring", "Team Pine"];

    /// <summary>
    /// Teams are known by their captain on the pitch ("Team Vic"), so name them that way. Falls back
    /// to the colour palette when the captain has no usable display name.
    /// </summary>
    private static string BuildTeamName(
        Guid captainId,
        int index,
        IReadOnlyList<RosterMemberRecord> roster)
    {
        var captain = roster.FirstOrDefault(member => member.PlayerProfileId == captainId);
        var firstName = captain?.DisplayName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstName)
            ? FallbackTeamNames[index % FallbackTeamNames.Length]
            : $"Team {firstName}";
    }

    public async Task<GameDayMutationModel> HandleAsync(
        AssignSessionCaptainsCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(
            sessionRepository,
            command.SessionId,
            cancellationToken);
        GameDayWorkflowQueries.EnsureCaptainDraftWindow(
            session,
            clock.UtcNow,
            GameDayWorkflowAuthorization.IsGameAdmin(currentUser));

        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            command.SessionId,
            cancellationToken);
        var eligibleIds = roster.Select(x => x.PlayerProfileId).ToHashSet();
        if (command.CaptainPlayerProfileIds.Any(x => !eligibleIds.Contains(x)))
        {
            throw new ApplicationConflictException("Captains must be selected from confirmed (Going or Waitlist) players.");
        }

        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken);
        if (match is not null)
        {
            GameDayWorkflowQueries.EnsureTeamsEditable(
                match,
                GameDayWorkflowAuthorization.IsGameAdmin(currentUser));

            var currentTeams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
            var currentAssignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
            var orderedCurrentCaptainIds = currentTeams
                .OrderBy(x => x.TeamNumber)
                .Where(x => x.CaptainPlayerProfileId.HasValue)
                .Select(x => x.CaptainPlayerProfileId!.Value)
                .ToArray();
            var currentCaptainIds = orderedCurrentCaptainIds.ToHashSet();
            if (currentTeams.Count == command.CaptainCount
                && orderedCurrentCaptainIds.SequenceEqual(command.CaptainPlayerProfileIds))
            {
                return new GameDayMutationModel(command.SessionId, match.Id, 0);
            }

            var hasRecordedFacts = (await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken)).Count > 0
                || (await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken)).Count > 0;
            if (hasRecordedFacts)
            {
                // Results and events point at these MatchTeam rows; rebuilding the topology would
                // orphan them. Moving players between the existing teams is still allowed.
                throw new ApplicationConflictException(
                    "Team count cannot change after results or stats have been recorded.");
            }

            // A game admin may re-cut the teams (say 4 down to 3), which necessarily discards the
            // current draft picks. Captains only get the no-op path above.
            if (currentAssignments.Any(x => !currentCaptainIds.Contains(x.PlayerProfileId))
                && !GameDayWorkflowAuthorization.IsGameAdmin(currentUser))
            {
                throw new ApplicationConflictException("Captain topology cannot change after player drafting has started.");
            }
        }
        else
        {
            match = new Match
            {
                Id = Guid.NewGuid(),
                SessionId = command.SessionId,
                MatchNumber = 1,
                Status = MatchStatus.Draft,
            };
        }

        var teams = command.CaptainPlayerProfileIds.Select((captainId, index) => new MatchTeam
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            TeamNumber = index + 1,
            Name = BuildTeamName(captainId, index, roster),
            CaptainPlayerProfileId = captainId,
        }).ToArray();
        var assignments = teams.Select(team => new TeamAssignment
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            MatchTeamId = team.Id,
            PlayerProfileId = team.CaptainPlayerProfileId!.Value,
        }).ToArray();
        var participants = assignments.Select(assignment => new PlayerMatchStats
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            PlayerProfileId = assignment.PlayerProfileId,
            Played = true,
            Started = true,
        }).ToArray();

        session.TeamCount = command.CaptainCount;

        if (await statsRepository.FindMatchAsync(match.Id, cancellationToken) is null)
        {
            await statsRepository.CreateMatchAsync(match, teams, assignments, participants, cancellationToken);
        }
        else
        {
            await statsRepository.ReplaceCaptainTopologyAsync(
                match.Id,
                teams,
                assignments,
                participants,
                cancellationToken);
        }

        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "Session.Captains.Assign",
            EntityName = nameof(Session),
            EntityId = session.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                matchId = match.Id,
                captainCount = command.CaptainCount,
                captainPlayerProfileIds = command.CaptainPlayerProfileIds,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GameDayMutationModel(command.SessionId, match.Id, command.CaptainCount);
    }
}

public sealed class LockSessionTeamsCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<LockSessionTeamsCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        LockSessionTeamsCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        GameDayWorkflowQueries.EnsureTeamLockWindow(
            session,
            clock.UtcNow,
            GameDayWorkflowAuthorization.IsGameAdmin(currentUser));
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Captain assignments were not found for this session.");
        if (match.Status == MatchStatus.InProgress)
        {
            return new GameDayMutationModel(session.Id, match.Id, 0);
        }

        if (match.Status != MatchStatus.Draft)
        {
            throw new ApplicationConflictException("Teams are already locked for this match.");
        }

        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        var assignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        if (teams.Count is < 2 or > 4
            || teams.Any(team => team.CaptainPlayerProfileId is not { } captainId
                || !assignments.Any(assignment => assignment.MatchTeamId == team.Id
                    && assignment.PlayerProfileId == captainId)))
        {
            throw new ApplicationConflictException("Every team must have its assigned captain before teams can lock.");
        }

        var eligibleIds = (await GameDayWorkflowQueries.ListEligibleRosterAsync(
                rsvpRepository,
                pickupPalGameRepository,
                playerProfileRepository,
                session.Id,
                cancellationToken))
            .Select(x => x.PlayerProfileId)
            .ToHashSet();
        if (assignments.Any(x => !eligibleIds.Contains(x.PlayerProfileId)))
        {
            throw new ApplicationConflictException("Only confirmed (Going or Waitlist) players can be included in the locked team roster.");
        }

        match.Status = MatchStatus.InProgress;
        match.StartedAtUtc ??= session.StartsAtUtc;
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "TeamDraft.Lock",
            EntityName = nameof(Match),
            EntityId = match.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                matchId = match.Id,
                teamCount = teams.Count,
                assignmentCount = assignments.Count,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(session.Id, match.Id, 1);
    }
}

public sealed class UnlockSessionTeamsCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<UnlockSessionTeamsCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        UnlockSessionTeamsCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Captain assignments were not found for this session.");
        // No upper time bound, mirroring Lock (which an admin may do whenever the session is published):
        // unlock is already fully constrained by requiring InProgress with no recorded results/stats, so
        // the button's CanUnlockTeams and this handler agree.
        if (match.Status == MatchStatus.Draft)
        {
            return new GameDayMutationModel(session.Id, match.Id, 0);
        }

        if (match.Status != MatchStatus.InProgress)
        {
            // Completed / NeedsReview / Published / Locked all mean the post-game has started; those
            // are undone through the post-game reopen flow, not by unlocking the teams.
            throw new ApplicationConflictException("Results have been recorded. Reopen the game from the post-game screen instead.");
        }

        if ((await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken)).Count > 0
            || (await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken)).Count > 0)
        {
            throw new ApplicationConflictException("Results or stats have been recorded; reopen from the post-game screen instead.");
        }

        // Back to Draft so captains can pick again; the game has not really started, so clear the mark.
        match.Status = MatchStatus.Draft;
        match.StartedAtUtc = null;
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "TeamDraft.Unlock",
            EntityName = nameof(Match),
            EntityId = match.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                matchId = match.Id,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(session.Id, match.Id, 1);
    }
}

public sealed class GetTeamDraftQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository)
{
    public async Task<TeamDraftModel> HandleAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, sessionId, cancellationToken);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Captain assignments were not found for this session.");
        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);

        // A game admin (or coordinator) can draft on behalf of any captain; a captain edits only
        // their own team. Admins default to the first team and switch client-side via TeamDraftModel.Teams.
        var isGameAdmin = GameDayWorkflowAuthorization.IsGameAdmin(currentUser);
        var actorTeam = teams.SingleOrDefault(x => x.CaptainPlayerProfileId == actor.Id);
        if (actorTeam is null)
        {
            if (!isGameAdmin)
            {
                throw new ApplicationForbiddenException("Only an assigned captain or game admin can draft this session's teams.");
            }

            actorTeam = teams.OrderBy(x => x.TeamNumber).FirstOrDefault()
                ?? throw new ApplicationNotFoundException("Captain assignments were not found for this session.");
        }

        var assignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            sessionId,
            cancellationToken);
        // An admin keeps editing after kick-off (and after results are recorded) until the match is
        // published or locked; a captain's sheet freezes as soon as the match leaves Draft.
        var locked = isGameAdmin
            ? match.Status is MatchStatus.Published or MatchStatus.Locked
                || !GameDayWorkflowQueries.IsAdminTeamEditOpen(session, clock.UtcNow)
            : match.Status != MatchStatus.Draft
                || GameDayWorkflowQueries.IsPostGameOpen(session, clock.UtcNow);
        var captainName = actorTeam.CaptainPlayerProfileId == actor.Id
            ? actor.DisplayName
            : roster.FirstOrDefault(member => member.PlayerProfileId == actorTeam.CaptainPlayerProfileId)?.DisplayName
                ?? "Captain";

        return new TeamDraftModel(
            sessionId,
            match.Id,
            actorTeam.Id,
            actorTeam.Name,
            captainName,
            !locked,
            locked,
            teams.Count,
            GameDayWorkflowQueries.ToRosterModels(roster),
            GameDayWorkflowQueries.ToTeamModels(teams, assignments, roster),
            isGameAdmin);
    }
}

public sealed class SaveCaptainTeamPicksCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<SaveCaptainTeamPicksCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        SaveCaptainTeamPicksCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(
            sessionRepository,
            command.SessionId,
            cancellationToken);
        GameDayWorkflowQueries.EnsureCaptainDraftWindow(
            session,
            clock.UtcNow,
            GameDayWorkflowAuthorization.IsGameAdmin(currentUser));
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Captain assignments were not found for this session.");
        GameDayWorkflowQueries.EnsureTeamsEditable(
            match,
            GameDayWorkflowAuthorization.IsGameAdmin(currentUser));

        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        var team = teams.SingleOrDefault(x => x.Id == command.MatchTeamId)
            ?? throw new ApplicationNotFoundException("Match team was not found.");
        if (team.CaptainPlayerProfileId != actor.Id
            && !GameDayWorkflowAuthorization.IsGameAdmin(currentUser))
        {
            throw new ApplicationForbiddenException("Only the team's captain or a game admin can update this team.");
        }

        var eligibleIds = (await GameDayWorkflowQueries.ListEligibleRosterAsync(
                rsvpRepository,
                pickupPalGameRepository,
                playerProfileRepository,
                command.SessionId,
                cancellationToken))
            .Select(x => x.PlayerProfileId)
            .ToHashSet();
        // Always keep the team's own captain on the roster, even when a game admin drafts on their behalf.
        var requestedIds = command.PlayerProfileIds
            .Append(team.CaptainPlayerProfileId ?? actor.Id)
            .Distinct()
            .ToArray();
        if (requestedIds.Any(x => !eligibleIds.Contains(x)))
        {
            throw new ApplicationConflictException("Team picks must be selected from confirmed (Going or Waitlist) players.");
        }

        var assignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        var conflictingAssignment = assignments.FirstOrDefault(x =>
            x.MatchTeamId != team.Id && requestedIds.Contains(x.PlayerProfileId));
        if (conflictingAssignment is not null)
        {
            throw new ApplicationConflictException("A selected player has already been drafted by another team.");
        }

        await statsRepository.ReplaceTeamAssignmentsAsync(
            match.Id,
            team.Id,
            requestedIds,
            cancellationToken);
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "TeamDraft.Picks.Replace",
            EntityName = nameof(MatchTeam),
            EntityId = team.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                matchId = match.Id,
                matchTeamId = team.Id,
                playerProfileIds = requestedIds,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GameDayMutationModel(session.Id, match.Id, requestedIds.Length);
    }
}

public sealed class GetPostGameApprovalQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository)
{
    public async Task<PostGameApprovalModel> HandleAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, sessionId, cancellationToken);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found for this session.");
        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        GameDayWorkflowAuthorization.EnsureCaptainOrGameAdmin(currentUser, actor.Id, teams);
        var events = await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken);
        var referencedPlayerIds = events
            .SelectMany(x => new[] { x.PlayerProfileId, x.AssistPlayerProfileId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var profiles = (await playerProfileRepository.ListProfilesAsync(referencedPlayerIds, cancellationToken))
            .ToDictionary(x => x.Id);
        var results = await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken);
        var assignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
        // A Draft game that was played but never explicitly locked can still be finalized by an admin -
        // recording the first result auto-locks it. Enable the screen for an admin when the teams are
        // lockable (2-4 teams, each with its captain assigned); otherwise it stays read-only.
        var teamsLockable = teams.Count is >= 2 and <= 4
            && teams.All(team => team.CaptainPlayerProfileId is { } captainId
                && assignments.Any(a => a.MatchTeamId == team.Id && a.PlayerProfileId == captainId));
        var canApprove = GameDayWorkflowQueries.IsPostGameOpen(session, clock.UtcNow)
            && match.Status is not MatchStatus.NeedsReview
                and not MatchStatus.Published
                and not MatchStatus.Locked
            && (match.Status != MatchStatus.Draft
                || (GameDayWorkflowAuthorization.IsGameAdmin(currentUser) && teamsLockable));

        return new PostGameApprovalModel(
            sessionId,
            match.Id,
            canApprove,
            match.Status is MatchStatus.Published or MatchStatus.Locked,
            match.Status == MatchStatus.NeedsReview,
            teams.Count,
            teams.OrderBy(x => x.TeamNumber).Select(team =>
            {
                var result = results.SingleOrDefault(x => x.MatchTeamId == team.Id);
                return new GameDayTeamResultModel(
                    team.Id,
                    team.Name,
                    result?.Wins ?? 0,
                    result?.Draws ?? 0,
                    result?.Losses ?? 0);
            }).ToArray(),
            events.Select(x =>
                {
                    // A self-submitted assist is stored as a Goal event with no scorer and only the
                    // assister set. Show the assister as the row's player and label it an assist, so
                    // it reads as "{assister} - Assist" instead of "Unknown player - Goal".
                    var isAssistOnly = x.EventType == MatchEventType.Goal
                        && x.PlayerProfileId is null
                        && x.AssistPlayerProfileId is not null;
                    var primaryPlayerId = x.PlayerProfileId ?? (isAssistOnly ? x.AssistPlayerProfileId : null);
                    return new PendingStatApprovalModel(
                        x.Id,
                        GameDayWorkflowQueries.ToPlayerModel(primaryPlayerId, profiles),
                        x.EventType == MatchEventType.Goal && x.PlayerProfileId is not null ? 1 : 0,
                        isAssistOnly ? 1 : 0,
                        x.ReviewStatus switch
                        {
                            MatchEventReviewStatus.Approved => "Approved",
                            MatchEventReviewStatus.Rejected => "NeedsReview",
                            _ => "Pending",
                        },
                        !isAssistOnly && x.AssistPlayerProfileId is { } assistId
                            ? GameDayWorkflowQueries.ToPlayerModel(assistId, profiles)
                            : null,
                        isAssistOnly ? "Assist" : x.EventType switch
                        {
                            MatchEventType.OwnGoal => "Own goal",
                            MatchEventType.YellowCard => "Yellow card",
                            MatchEventType.RedCard => "Red card",
                            _ => "Goal",
                        });
                })
                .ToArray(),
            // A game admin can reopen a conflicted scoreline to re-enter the correct results.
            match.Status == MatchStatus.NeedsReview
                && GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
                && GameDayWorkflowQueries.IsPostGameOpen(session, clock.UtcNow),
            session.Title,
            session.StartsAtUtc);
    }
}

/// <summary>
/// Clears a conflicted scoreline (NeedsReview) back to InProgress so a game admin can re-enter the
/// correct team results, which then re-run the consistency check. Without this a NeedsReview match
/// is a dead-end: results cannot be re-saved and it can never be published.
/// </summary>
public sealed class ReopenPostGameResultsCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        ReopenPostGameResultsCommand command,
        CancellationToken cancellationToken = default)
    {
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        GameDayWorkflowQueries.EnsurePostGameWindow(session, clock.UtcNow);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found for this session.");
        if (match.Status != MatchStatus.NeedsReview)
        {
            throw new ApplicationConflictException("Only a match under review can be reopened.");
        }

        match.Status = MatchStatus.InProgress;
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "PostGame.Results.Reopen",
            EntityName = nameof(Match),
            EntityId = match.Id,
            DetailsJson = JsonSerializer.Serialize(new { sessionId = session.Id, matchId = match.Id }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(session.Id, match.Id, 1);
    }
}

/// <summary>
/// Attaches an unlinked imported Pickup Pal participant (guests, or players whose phone/name never
/// matched at import) to a real player profile, so they appear on the roster and can submit stats
/// and be rated. Guarded to game admins; the participant must not already be linked.
/// </summary>
public sealed class LinkParticipantToProfileCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        LinkParticipantToProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var participant = await pickupPalGameRepository.FindParticipantAsync(command.ParticipantId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Imported participant was not found.");
        var profile = await playerProfileRepository.GetByIdAsync(command.PlayerProfileId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        if (participant.PlayerProfileId == profile.Id)
        {
            return new GameDayMutationModel(participant.SessionId, Guid.Empty, 0);
        }

        var participantsOnSession = await pickupPalGameRepository.ListParticipantsAsync(
            participant.SessionId,
            cancellationToken);
        if (participantsOnSession.Any(x => x.Id != participant.Id && x.PlayerProfileId == profile.Id))
        {
            throw new ApplicationConflictException("That player is already linked to another entry on this game.");
        }

        participant.PlayerProfileId = profile.Id;
        pickupPalGameRepository.UpdateParticipant(participant);
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "PickupPalParticipant.Link",
            EntityName = nameof(PickupPalGameParticipant),
            EntityId = participant.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = participant.SessionId,
                participantId = participant.Id,
                playerProfileId = profile.Id,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(participant.SessionId, Guid.Empty, 1);
    }
}

public sealed class ApprovePostGameStatCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<ApprovePostGameStatCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    ReviewMatchEventCommandHandler reviewMatchEventHandler)
{
    public async Task<GameDayMutationModel> HandleAsync(
        ApprovePostGameStatCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        GameDayWorkflowQueries.EnsurePostGameWindow(session, clock.UtcNow);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found for this session.");
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        GameDayWorkflowAuthorization.EnsureCaptainOrGameAdmin(currentUser, actor.Id, teams);
        if (match.Status is MatchStatus.Published or MatchStatus.Locked)
        {
            throw new ApplicationConflictException("Published match events can be changed only through a stat correction.");
        }

        if (match.Status == MatchStatus.Draft)
        {
            throw new ApplicationConflictException("Lock teams before recording post-game results.");
        }

        var matchEvent = await statsRepository.FindMatchEventAsync(command.MatchEventId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match event was not found.");
        if (matchEvent.MatchId != match.Id)
        {
            throw new ApplicationNotFoundException("Match event was not found.");
        }

        if (matchEvent.ReviewStatus == MatchEventReviewStatus.Approved)
        {
            return new GameDayMutationModel(session.Id, match.Id, 0);
        }

        var result = await reviewMatchEventHandler.HandleAsync(
            new ReviewMatchEventCommand(match.Id, matchEvent.Id, true, null),
            cancellationToken);
        return new GameDayMutationModel(session.Id, result.MatchId, result.AffectedCount);
    }
}

public sealed class SavePostGameTeamResultCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<SavePostGameTeamResultCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        SavePostGameTeamResultCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        GameDayWorkflowQueries.EnsurePostGameWindow(session, clock.UtcNow);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found for this session.");
        if (match.Status is MatchStatus.Published or MatchStatus.Locked)
        {
            throw new ApplicationConflictException("Published results can be changed only through a stat correction.");
        }

        if (match.Status == MatchStatus.Draft)
        {
            // Finalize-on-record: an admin recording results on a played-but-unlocked game locks the
            // teams in place (Draft -> InProgress) rather than being turned away. A game may have been
            // played without an explicit "Lock teams" step, and Recent Games has no lock affordance.
            // Captains still cannot auto-lock.
            if (!GameDayWorkflowAuthorization.IsGameAdmin(currentUser))
            {
                throw new ApplicationConflictException("Ask a game admin to lock the teams before recording results.");
            }

            var draftTeams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
            var draftAssignments = await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken);
            if (draftTeams.Count is < 2 or > 4
                || draftTeams.Any(t => t.CaptainPlayerProfileId is not { } captainId
                    || !draftAssignments.Any(a => a.MatchTeamId == t.Id && a.PlayerProfileId == captainId)))
            {
                throw new ApplicationConflictException("Every team needs its assigned captain before results can be recorded.");
            }

            match.Status = MatchStatus.InProgress;
            match.StartedAtUtc ??= session.StartsAtUtc;
            await auditLogRepository.AddAsync(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                ActorType = AuditActorType.PlayerProfile,
                ActorPlayerProfileId = actor.Id,
                Action = "TeamDraft.Lock.OnResult",
                EntityName = nameof(Match),
                EntityId = match.Id,
                DetailsJson = JsonSerializer.Serialize(new { sessionId = session.Id, matchId = match.Id }),
                OccurredAtUtc = clock.UtcNow,
            }, cancellationToken);
        }

        if (match.Status == MatchStatus.NeedsReview)
        {
            throw new ApplicationConflictException("Resolve the match review conflict before recording more results.");
        }

        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        var team = teams.SingleOrDefault(x => x.Id == command.MatchTeamId)
            ?? throw new ApplicationNotFoundException("Match team was not found.");
        if (!GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
            && team.CaptainPlayerProfileId != actor.Id)
        {
            throw new ApplicationForbiddenException("Captains can record results only for their assigned team.");
        }

        // A rotation can play many more games than a side has opponents, so the only per-team bound
        // is a sanity ceiling; whether the full set balances is decided by AreConsistent below.
        if (command.Wins + command.Draws + command.Losses > GameDayResultRules.MaxGamesPerTeam)
        {
            throw new ApplicationConflictException(
                $"A team cannot record more than {GameDayResultRules.MaxGamesPerTeam} games.");
        }

        var existingResults = await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken);
        var existing = existingResults.SingleOrDefault(x => x.MatchTeamId == team.Id);
        if (existing is not null
            && existing.Wins == command.Wins
            && existing.Draws == command.Draws
            && existing.Losses == command.Losses)
        {
            return new GameDayMutationModel(session.Id, match.Id, 0);
        }

        await statsRepository.UpsertMatchResultsAsync(match.Id, [new MatchResult
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            MatchTeamId = team.Id,
            Wins = command.Wins,
            Draws = command.Draws,
            Losses = command.Losses,
        }], cancellationToken);
        var requestedResult = new GameDayTeamResultModel(
            team.Id,
            team.Name,
            command.Wins,
            command.Draws,
            command.Losses);
        var projectedResults = teams.Select(currentTeam =>
        {
            if (currentTeam.Id == team.Id)
            {
                return requestedResult;
            }

            var current = existingResults.SingleOrDefault(x => x.MatchTeamId == currentTeam.Id);
            return new GameDayTeamResultModel(
                currentTeam.Id,
                currentTeam.Name,
                current?.Wins ?? 0,
                current?.Draws ?? 0,
                current?.Losses ?? 0);
        }).ToArray();
        var submittedResultCount = existing is null ? existingResults.Count + 1 : existingResults.Count;
        if (submittedResultCount == teams.Count
            && GameDayResultRules.AreComplete(projectedResults, teams.Count))
        {
            if (GameDayResultRules.AreConsistent(projectedResults))
            {
                match.Status = MatchStatus.Completed;
                match.CompletedAtUtc = clock.UtcNow;
            }
            else
            {
                match.Status = MatchStatus.NeedsReview;
                await statsRepository.AddStatCorrectionAsync(new StatCorrection
                {
                    Id = Guid.NewGuid(),
                    MatchId = match.Id,
                    CorrectedByPlayerProfileId = actor.Id,
                    Reason = "Conflicting team result submissions.",
                    BeforeJson = JsonSerializer.Serialize(existingResults.Select(x => new
                    {
                        x.MatchTeamId,
                        x.Wins,
                        x.Draws,
                        x.Losses,
                    })),
                    AfterJson = JsonSerializer.Serialize(projectedResults),
                    CorrectedAtUtc = clock.UtcNow,
                }, cancellationToken);
            }
        }

        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "PostGame.TeamResult.Record",
            EntityName = nameof(MatchTeam),
            EntityId = team.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                matchId = match.Id,
                matchTeamId = team.Id,
                command.Wins,
                command.Draws,
                command.Losses,
            }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GameDayMutationModel(session.Id, match.Id, 1);
    }
}

public sealed class PublishPostGameCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<PublishPostGameCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IStatsRepository statsRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<GameDayMutationModel> HandleAsync(
        PublishPostGameCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await GameDayWorkflowAuthorization.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var session = await GameDayWorkflowQueries.GetSessionAsync(sessionRepository, command.SessionId, cancellationToken);
        GameDayWorkflowQueries.EnsurePostGameWindow(session, clock.UtcNow);
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found for this session.");
        var teams = await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        GameDayWorkflowAuthorization.EnsureCaptainOrGameAdmin(currentUser, actor.Id, teams);

        if (match.Status == MatchStatus.Published)
        {
            return new GameDayMutationModel(session.Id, match.Id, 0);
        }

        if (match.Status == MatchStatus.Locked)
        {
            throw new ApplicationConflictException("Locked match stats can be changed only through a stat correction.");
        }

        if (match.Status == MatchStatus.NeedsReview)
        {
            throw new ApplicationConflictException("Resolve the match review conflict before publishing.");
        }

        if (match.Status == MatchStatus.Draft)
        {
            throw new ApplicationConflictException("Lock teams before publishing post-game results.");
        }

        var results = await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken);
        if (teams.Count == 0 || results.Count != teams.Count)
        {
            throw new ApplicationConflictException("Every team must have a recorded result before publishing.");
        }

        var projectedResults = teams.Select(team =>
        {
            var result = results.Single(x => x.MatchTeamId == team.Id);
            return new GameDayTeamResultModel(
                team.Id,
                team.Name,
                result.Wins,
                result.Draws,
                result.Losses);
        }).ToArray();
        if (!GameDayResultRules.AreComplete(projectedResults, teams.Count)
            || !GameDayResultRules.AreConsistent(projectedResults))
        {
            throw new ApplicationConflictException("Team results must describe one consistent outcome for every rotation before publishing.");
        }

        var events = await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken);
        if (events.Any(x => x.ReviewStatus == MatchEventReviewStatus.Pending))
        {
            throw new ApplicationConflictException("Every submitted match event must be reviewed before publishing.");
        }

        match.Status = MatchStatus.Published;
        match.CompletedAtUtc ??= clock.UtcNow;
        await auditLogRepository.AddAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            ActorType = AuditActorType.PlayerProfile,
            ActorPlayerProfileId = actor.Id,
            Action = "PostGame.Publish",
            EntityName = nameof(Match),
            EntityId = match.Id,
            DetailsJson = JsonSerializer.Serialize(new { sessionId = session.Id, matchId = match.Id }),
            OccurredAtUtc = clock.UtcNow,
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GameDayMutationModel(session.Id, match.Id, 1);
    }
}

internal static class GameDayWorkflowAuthorization
{
    private const string CanManageSessionsPolicy = "CanManageSessions";

    internal static bool IsGameAdmin(ICurrentUser currentUser) =>
        currentUser.HasPolicy(CanManageSessionsPolicy)
        || currentUser.IsInRole("Owner")
        || currentUser.IsInRole("Admin")
        || currentUser.IsInRole("GameAdmin");

    internal static void EnsureGameAdmin(ICurrentUser currentUser)
    {
        if (!IsGameAdmin(currentUser))
        {
            throw new ApplicationForbiddenException("Only game admins can assign captains.");
        }
    }

    internal static void EnsureCaptainOrGameAdmin(
        ICurrentUser currentUser,
        Guid actorPlayerProfileId,
        IReadOnlyList<MatchTeam> teams)
    {
        if (!IsGameAdmin(currentUser) && !teams.Any(x => x.CaptainPlayerProfileId == actorPlayerProfileId))
        {
            throw new ApplicationForbiddenException("Only assigned captains or game admins can manage post-game review.");
        }
    }

    internal static async Task<PlayerProfile> GetCurrentProfileAsync(
        ICurrentUser currentUser,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        return await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
    }
}

internal static class GameDayWorkflowQueries
{
    private static readonly TimeSpan PostGameOffset = TimeSpan.FromMinutes(90);

    internal static async Task<Session> GetSessionAsync(
        ISessionRepository sessionRepository,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");
        if (session.Status == SessionStatus.Canceled)
        {
            throw new ApplicationConflictException("Canceled sessions cannot run game-day workflows.");
        }

        return session;
    }

    internal static bool IsPostGameOpen(Session session, DateTime nowUtc) =>
        nowUtc >= session.StartsAtUtc.Add(PostGameOffset);

    /// <summary>
    /// How long after kickoff a game admin can still correct teams. Rosters get sorted out after the
    /// fact - someone swapped sides, a late arrival was put on the wrong team - so admins keep an
    /// editing window for a few days rather than losing access 90 minutes in.
    /// </summary>
    internal static readonly TimeSpan AdminTeamEditWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// Game admins own the team sheet from publish until <see cref="AdminTeamEditWindow"/> after
    /// kickoff, so they can both set up in advance and fix things afterwards.
    /// </summary>
    internal static bool IsAdminTeamEditOpen(Session session, DateTime nowUtc) =>
        session.Status == SessionStatus.Published
        && nowUtc <= session.StartsAtUtc.Add(AdminTeamEditWindow);

    /// <summary>
    /// Game admins set teams up ahead of time and correct them afterwards; captains only act during
    /// the game-day window, from check-in until post-game opens.
    /// </summary>
    internal static void EnsureCaptainDraftWindow(Session session, DateTime nowUtc, bool isGameAdmin)
    {
        if (session.Status != SessionStatus.Published)
        {
            throw new ApplicationConflictException("Only published sessions can assign or draft teams.");
        }

        if (isGameAdmin)
        {
            if (!IsAdminTeamEditOpen(session, nowUtc))
            {
                throw new ApplicationConflictException(
                    $"Team editing closed {AdminTeamEditWindow.TotalDays:0} days after kick-off.");
            }

            return;
        }

        if (nowUtc < session.CheckInOpensAtUtc)
        {
            throw new ApplicationConflictException("Captain assignment opens with game-day check-in.");
        }

        if (IsPostGameOpen(session, nowUtc))
        {
            throw new ApplicationConflictException("Team drafting is closed for this session.");
        }
    }

    /// <summary>
    /// Team composition stays editable until the match is published or locked - those are settled
    /// facts that move only through a stat correction. Captains additionally only edit a Draft
    /// match; an admin may still rearrange one that has results recorded.
    /// </summary>
    internal static void EnsureTeamsEditable(Match match, bool isGameAdmin)
    {
        if (match.Status is MatchStatus.Published or MatchStatus.Locked)
        {
            throw new ApplicationConflictException(
                "Published teams can be changed only through a stat correction.");
        }

        if (!isGameAdmin && match.Status != MatchStatus.Draft)
        {
            throw new ApplicationConflictException("Team drafting is locked for this match.");
        }
    }

    internal static void EnsureTeamLockWindow(Session session, DateTime nowUtc, bool isGameAdmin)
    {
        if (session.Status != SessionStatus.Published)
        {
            throw new ApplicationConflictException("Only published sessions can lock teams.");
        }

        if (!isGameAdmin && nowUtc < session.CheckInOpensAtUtc)
        {
            throw new ApplicationConflictException("Team locking opens with game-day check-in.");
        }
    }

    /// <summary>
    /// True when the actor may act on the pre-game team workflow now: published, not yet post-game,
    /// and either a game admin (any time after publish) or anyone else once check-in has opened.
    /// </summary>
    internal static bool IsTeamSetupOpen(Session session, DateTime nowUtc, bool isGameAdmin) =>
        isGameAdmin
            ? IsAdminTeamEditOpen(session, nowUtc)
            : session.Status == SessionStatus.Published
              && nowUtc >= session.CheckInOpensAtUtc
              && !IsPostGameOpen(session, nowUtc);

    internal static void EnsurePostGameWindow(Session session, DateTime nowUtc)
    {
        if (!IsPostGameOpen(session, nowUtc))
        {
            throw new ApplicationConflictException("Post-game review is not open yet.");
        }
    }

    internal static async Task<IReadOnlyList<RosterMemberRecord>> ListEligibleRosterAsync(
        IRsvpRepository rsvpRepository,
        IPickupPalGameRepository pickupPalGameRepository,
        IPlayerProfileRepository playerProfileRepository,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // Team eligibility is Going + Waitlist (check-in is a separate attendance fact): local RSVP
        // Going and active Waitlist rows unioned with linked imported Pickup Pal participants, deduped
        // by profile id with local rows and Going taking precedence.
        var going = await rsvpRepository.ListGoingRosterAsync(sessionId, cancellationToken);
        var waitlist = await rsvpRepository.ListActiveWaitlistRosterAsync(sessionId, cancellationToken);
        var imported = await pickupPalGameRepository.ListParticipantsAsync(sessionId, cancellationToken);

        var localIds = going.Select(member => member.PlayerProfileId)
            .Concat(waitlist.Select(member => member.PlayerProfileId))
            .ToHashSet();
        var importedLinked = imported
            .Where(participant => participant.PlayerProfileId is { } linkedId && !localIds.Contains(linkedId))
            .ToArray();

        // Once a row is linked, the profile is the identity: show the player's registered name rather
        // than the WhatsApp handle the import captured ("tob8"), which is what the roster showed even
        // after a match. The imported name stays as the fallback for a profile with no name set.
        var profilesById = importedLinked.Length == 0
            ? new Dictionary<Guid, PlayerProfile>()
            : (await playerProfileRepository.ListProfilesAsync(
                    importedLinked.Select(participant => participant.PlayerProfileId!.Value).Distinct().ToArray(),
                    cancellationToken))
                .ToDictionary(profile => profile.Id);

        var importedMembers = importedLinked
            .Select(participant =>
            {
                var profile = profilesById.GetValueOrDefault(participant.PlayerProfileId!.Value);
                return new RosterMemberRecord(
                    participant.PlayerProfileId!.Value,
                    string.IsNullOrWhiteSpace(profile?.DisplayName)
                        ? participant.DisplayName
                        : profile.DisplayName,
                    profile?.PreferredPosition ?? string.Empty,
                    participant.IsGuest,
                    participant.IsWaitlist ? participant.DisplayOrder + 1 : null);
            });

        return going
            .Concat(waitlist)
            .Concat(importedMembers)
            .GroupBy(member => member.PlayerProfileId)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// The roster as displayed: everyone from <see cref="ListEligibleRosterAsync"/> plus the
    /// imported participants that never linked to a profile.
    /// <para>
    /// Deliberately separate from the eligible roster rather than widening it. Eligibility drives
    /// captain assignment and check-in, both keyed on a profile id, so an unlinked participant must
    /// never leak into those; but a count shown next to a list has to describe the same population
    /// as the list, which is what this query provides.
    /// </para>
    /// </summary>
    internal static async Task<IReadOnlyList<GameDayRosterEntryModel>> ListDisplayRosterAsync(
        IRsvpRepository rsvpRepository,
        IPickupPalGameRepository pickupPalGameRepository,
        IPlayerProfileRepository playerProfileRepository,
        Guid sessionId,
        IReadOnlySet<Guid> checkedInPlayerProfileIds,
        CancellationToken cancellationToken)
    {
        var eligible = await ListEligibleRosterAsync(
            rsvpRepository, pickupPalGameRepository, playerProfileRepository, sessionId, cancellationToken);
        var imported = await pickupPalGameRepository.ListParticipantsAsync(sessionId, cancellationToken);

        var linked = eligible
            .Select(member => new GameDayRosterEntryModel(
                member.PlayerProfileId,
                member.DisplayName,
                member.IsGuest,
                member.WaitlistPosition is not null,
                checkedInPlayerProfileIds.Contains(member.PlayerProfileId)));

        // An unlinked participant has no profile, so it can be neither checked in nor deduped by
        // profile id; the Pickup Pal participant id is its only stable identity.
        var unlinked = imported
            .Where(participant => participant.PlayerProfileId is null)
            .OrderBy(participant => participant.DisplayOrder)
            .Select(participant => new GameDayRosterEntryModel(
                PlayerProfileId: null,
                participant.DisplayName,
                participant.IsGuest,
                participant.IsWaitlist,
                IsCheckedIn: false,
                participant.PickupPalParticipantId));

        return [.. linked, .. unlinked];
    }

    internal static IReadOnlyList<CheckedInGameDayPlayerModel> ToRosterModels(
        IReadOnlyList<RosterMemberRecord> roster) =>
        roster.Select(x => new CheckedInGameDayPlayerModel(
            ToPlayerModel(x),
            x.WaitlistPosition is not null
                ? "Waitlist"
                : string.IsNullOrWhiteSpace(x.PreferredPosition) ? "Going" : x.PreferredPosition))
            .ToArray();

    internal static GameDayRosterPlayerModel ToPlayerModel(RosterMemberRecord player) =>
        new(
            player.PlayerProfileId,
            player.DisplayName,
            BuildInitials(player.DisplayName),
            player.PreferredPosition,
            player.IsGuest);

    internal static GameDayRosterPlayerModel ToPlayerModel(
        Guid? playerProfileId,
        IReadOnlyDictionary<Guid, PlayerProfile> profiles)
    {
        if (playerProfileId is { } id && profiles.TryGetValue(id, out var player))
        {
            return new GameDayRosterPlayerModel(
                player.Id,
                player.DisplayName,
                BuildInitials(player.DisplayName),
                player.PreferredPosition,
                player.IsGuest);
        }

        return new GameDayRosterPlayerModel(
            playerProfileId ?? Guid.Empty,
            "Unknown player",
            "?",
            string.Empty,
            false);
    }

    internal static IReadOnlyList<GameDayMatchTeamModel> ToTeamModels(
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<RosterMemberRecord> roster)
    {
        var names = roster.ToDictionary(x => x.PlayerProfileId, x => x.DisplayName);
        return teams.OrderBy(x => x.TeamNumber).Select(team => new GameDayMatchTeamModel(
            team.Id,
            team.Name,
            team.CaptainPlayerProfileId ?? Guid.Empty,
            team.CaptainPlayerProfileId is { } captainId && names.TryGetValue(captainId, out var name)
                ? name
                : "Captain",
            assignments
                .Where(x => x.MatchTeamId == team.Id)
                .Select(x => x.PlayerProfileId)
                .ToArray())).ToArray();
    }

    private static string BuildInitials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(words.Take(2).Select(x => char.ToUpperInvariant(x[0])));
    }
}

internal static class GameDayResultRules
{
    /// <summary>Sanity ceiling on one team's game count, to stop a stuck stepper writing nonsense.</summary>
    internal const int MaxGamesPerTeam = 30;

    /// <summary>
    /// Every team must have reported and at least one game must have been played. The number of
    /// games is deliberately not fixed: with three or four teams the night is usually a rotation
    /// (winner stays on), so a team can play many more games than there are opponents.
    /// </summary>
    internal static bool AreComplete(IReadOnlyList<GameDayTeamResultModel> results, int teamCount) =>
        results.Count == teamCount
        && results.Sum(x => x.Wins + x.Draws + x.Losses) > 0;

    /// <summary>
    /// Aggregate counters can only be checked against the identities every fixture list obeys:
    /// each game produces exactly one win and one loss, or two halves of a draw. With exactly two
    /// teams the pairing is unambiguous, so each side's record must mirror the other's.
    /// </summary>
    internal static bool AreConsistent(IReadOnlyList<GameDayTeamResultModel> results)
    {
        if (results.Any(x => x.Wins < 0 || x.Draws < 0 || x.Losses < 0))
        {
            return false;
        }

        if (results.Sum(x => x.Wins) != results.Sum(x => x.Losses))
        {
            return false;
        }

        // A draw is recorded by both teams, so the total is always even.
        if (results.Sum(x => x.Draws) % 2 != 0)
        {
            return false;
        }

        if (results.Count == 2)
        {
            var (first, second) = (results[0], results[1]);
            return first.Wins == second.Losses
                && first.Losses == second.Wins
                && first.Draws == second.Draws;
        }

        return true;
    }
}
