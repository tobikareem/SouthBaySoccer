using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Stats;

/// <summary>
/// Player-facing post-game reads (STAT-7 match stats, STAT-8 rate teammates). The write side that
/// decides which submitted facts count lives in the STAT-9 captain/game-admin workflow.
/// </summary>
public sealed class GetMyMatchStatsQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository)
{
    public async Task<MyMatchStatsModel> HandleAsync(
        GetMyMatchStatsQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var match = await statsRepository.FindMatchAsync(query.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        var events = await statsRepository.ListMatchEventsAsync(query.MatchId, cancellationToken);
        var teams = await statsRepository.ListMatchTeamsAsync(query.MatchId, cancellationToken);

        var mine = events.Where(x => x.SubmittedByPlayerProfileId == actor.Id).ToArray();
        var isPending = mine.Any(x => x.ReviewStatus == MatchEventReviewStatus.Pending);
        var isConfirmed = mine.Any(x => x.ReviewStatus == MatchEventReviewStatus.Approved);

        var submissions = await ProjectTeammateSubmissionsAsync(
            events,
            actor.Id,
            playerProfileRepository,
            cancellationToken);

        return new MyMatchStatsModel(
            match.Id,
            actor.Id,
            await BuildSubtitleAsync(match, sessionRepository, venueRepository, cancellationToken),
            MatchStatsProjection.CountGoals(mine, actor.Id),
            MatchStatsProjection.CountAssists(mine, actor.Id),
            isPending,
            // Once a submission is confirmed it is a settled fact; changing it is a stat correction,
            // not a resubmission.
            !isConfirmed && match.Status is not (MatchStatus.Published or MatchStatus.Locked),
            GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
                || teams.Any(team => team.CaptainPlayerProfileId == actor.Id),
            submissions);
    }

    private static async Task<IReadOnlyList<TeammateStatSubmissionModel>> ProjectTeammateSubmissionsAsync(
        IReadOnlyList<MatchEvent> events,
        Guid actorProfileId,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var submitterIds = events
            .Where(x => x.SubmittedByPlayerProfileId is { } submitter && submitter != actorProfileId)
            .Select(x => x.SubmittedByPlayerProfileId!.Value)
            .Distinct()
            .ToArray();
        if (submitterIds.Length == 0)
        {
            return [];
        }

        var profiles = await playerProfileRepository.ListProfilesAsync(submitterIds, cancellationToken);
        return profiles
            .Select(profile =>
            {
                var submitted = events
                    .Where(x => x.SubmittedByPlayerProfileId == profile.Id)
                    .ToArray();
                return new TeammateStatSubmissionModel(
                    profile.Id,
                    profile.DisplayName,
                    profile.PreferredPosition,
                    profile.IsGuest,
                    MatchStatsProjection.CountGoals(submitted, profile.Id),
                    MatchStatsProjection.CountAssists(submitted, profile.Id),
                    submitted.All(x => x.ReviewStatus == MatchEventReviewStatus.Approved));
            })
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string> BuildSubtitleAsync(
        Match match,
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(match.SessionId, cancellationToken);
        if (session is null)
        {
            return string.Empty;
        }

        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);
        var day = SessionAdminTimeZone.ToLocal(session.StartsAtUtc)
            .ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);
        return venue is null ? day : $"{day} - {venue.Name}";
    }
}

public sealed class GetRateableTeammatesQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository)
{
    public async Task<IReadOnlyList<RateableTeammateModel>> HandleAsync(
        GetRateableTeammatesQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var match = await statsRepository.FindMatchAsync(query.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        var session = await sessionRepository.GetByIdAsync(match.SessionId, cancellationToken);
        if (session is null || !PeerFeedbackWindow.IsOpen(session, clock.UtcNow))
        {
            return [];
        }

        // Everyone who was part of the session can rate everyone else they played with - not just
        // their own side - so the pool is the confirmed roster rather than one team's draft picks.
        var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            match.SessionId,
            cancellationToken);
        if (!roster.Any(member => member.PlayerProfileId == actor.Id))
        {
            return [];
        }

        // INV-8: the rater is never in their own rateable list, so the UI cannot offer a self vote.
        var teammateIds = roster
            .Select(member => member.PlayerProfileId)
            .Where(id => id != actor.Id)
            .Distinct()
            .ToArray();
        if (teammateIds.Length == 0)
        {
            return [];
        }

        var profiles = await playerProfileRepository.ListProfilesAsync(teammateIds, cancellationToken);
        var events = await statsRepository.ListMatchEventsAsync(query.MatchId, cancellationToken);
        var approved = events
            .Where(x => x.ReviewStatus == MatchEventReviewStatus.Approved)
            .ToArray();

        return profiles
            .Select(profile => new RateableTeammateModel(
                profile.Id,
                profile.DisplayName,
                profile.PreferredPosition,
                profile.IsGuest,
                MatchStatsProjection.DescribeTally(
                    MatchStatsProjection.CountGoals(approved, profile.Id),
                    MatchStatsProjection.CountAssists(approved, profile.Id))))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// STAT-7 self submission. The player reports their own tally; each goal and each assist becomes a
/// pending raw <see cref="MatchEvent"/> awaiting captain or game-admin confirmation.
/// </summary>
public sealed class SubmitMyMatchStatsCommandHandler(
    ICurrentUser currentUser,
    IValidator<SubmitMyMatchStatsCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(
        SubmitMyMatchStatsCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        if (match.Status is MatchStatus.Published or MatchStatus.Locked)
        {
            throw new ApplicationConflictException(
                "Published match stats can be changed only through a stat correction.");
        }

        var events = await statsRepository.ListMatchEventsAsync(command.MatchId, cancellationToken);
        if (events.Any(x => x.SubmittedByPlayerProfileId == actor.Id
            && x.ReviewStatus == MatchEventReviewStatus.Approved))
        {
            throw new ApplicationConflictException(
                "Your stats were already confirmed. Ask a captain or game admin for a correction.");
        }

        var assignments = await statsRepository.ListAssignmentsAsync(command.MatchId, cancellationToken);
        var matchTeamId = assignments
            .FirstOrDefault(x => x.PlayerProfileId == actor.Id)?.MatchTeamId;

        var submitted = new List<MatchEvent>(command.Goals + command.Assists);
        for (var i = 0; i < command.Goals; i++)
        {
            submitted.Add(CreateEvent(command.MatchId, matchTeamId, actor.Id, scorerId: actor.Id, assistId: null));
        }

        // Aggregate self-report (STAT-7 simple model): an assist is a goal credit with no named
        // scorer. Goal counts read the scorer column, assist counts read the assist column, so these
        // rows only ever add to this player's assist tally.
        for (var i = 0; i < command.Assists; i++)
        {
            submitted.Add(CreateEvent(command.MatchId, matchTeamId, actor.Id, scorerId: null, assistId: actor.Id));
        }

        await statsRepository.ReplaceOwnPendingMatchEventsAsync(
            command.MatchId,
            actor.Id,
            submitted,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, submitted.Count);
    }

    private static MatchEvent CreateEvent(
        Guid matchId,
        Guid? matchTeamId,
        Guid submittedById,
        Guid? scorerId,
        Guid? assistId) =>
        new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchTeamId = matchTeamId,
            PlayerProfileId = scorerId,
            AssistPlayerProfileId = assistId,
            EventType = MatchEventType.Goal,
            // Pickup games are not clocked, so every self-reported fact sits at minute zero.
            Minute = 0,
            SubmittedByPlayerProfileId = submittedById,
            ReviewStatus = MatchEventReviewStatus.Pending,
        };
}

/// <summary>
/// Confirms every pending row one player submitted, so a captain approves a teammate's whole tally
/// in one action instead of one raw event at a time.
/// </summary>
public sealed class ConfirmPlayerSubmissionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IValidator<ConfirmPlayerSubmissionCommand> validator,
    IPlayerProfileRepository playerProfileRepository,
    IStatsRepository statsRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<StatMutationResult> HandleAsync(
        ConfirmPlayerSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var match = await statsRepository.FindMatchAsync(command.MatchId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Match was not found.");
        var teams = await statsRepository.ListMatchTeamsAsync(command.MatchId, cancellationToken);
        GameDayWorkflowAuthorization.EnsureCaptainOrGameAdmin(currentUser, actor.Id, teams);

        if (match.Status is MatchStatus.Published or MatchStatus.Locked)
        {
            throw new ApplicationConflictException(
                "Published match events can be changed only through a stat correction.");
        }

        if (match.Status == MatchStatus.Draft)
        {
            throw new ApplicationConflictException("Lock teams before reviewing match events.");
        }

        var events = await statsRepository.ListMatchEventsAsync(command.MatchId, cancellationToken);
        var pending = events
            .Where(x => x.SubmittedByPlayerProfileId == command.PlayerProfileId
                && x.ReviewStatus == MatchEventReviewStatus.Pending)
            .ToArray();
        foreach (var matchEvent in pending)
        {
            matchEvent.ReviewStatus = MatchEventReviewStatus.Approved;
            matchEvent.ReviewedByPlayerProfileId = actor.Id;
            matchEvent.ReviewedAtUtc = clock.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new StatMutationResult(command.MatchId, pending.Length);
    }
}

/// <summary>
/// Finds the most recent match this player can still report stats for, so the Sessions tab can
/// prompt them. Open to anyone who was on the confirmed roster - being drafted onto a team is not
/// required, since a session may never have been drafted at all.
/// </summary>
public sealed class GetPendingStatSubmissionQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository)
{
    /// <summary>How long after kick-off a player can still report their own goals and assists.</summary>
    public static readonly TimeSpan SubmissionWindow = TimeSpan.FromDays(3);

    public async Task<PendingStatSubmissionModel?> HandleAsync(
        GetPendingStatSubmissionQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        var actor = await SubmitPeerFeedbackCommandHandler.GetCurrentProfileAsync(
            currentUser,
            playerProfileRepository,
            cancellationToken);
        var nowUtc = clock.UtcNow;
        var sessions = await sessionRepository.ListGameDayCandidatesAsync(
            nowUtc.Subtract(SubmissionWindow),
            nowUtc,
            cancellationToken);

        foreach (var session in sessions.OrderByDescending(x => x.StartsAtUtc))
        {
            // Only prompt once the game has actually been played.
            if (!GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc))
            {
                continue;
            }

            var match = await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken);
            if (match is null || match.Status is MatchStatus.Published or MatchStatus.Locked)
            {
                continue;
            }

            var roster = await GameDayWorkflowQueries.ListEligibleRosterAsync(
                rsvpRepository,
                pickupPalGameRepository,
                session.Id,
                cancellationToken);

            if (!roster.Any(member => member.PlayerProfileId == actor.Id))
            {
                // Not on the roster: if the game still has unclaimed imported entries this player
                // might be one of them, so surface the prompt anyway and route it to self-claim -
                // they link themselves first, then submit. If nothing is unclaimed there is no way
                // to place them, so move on.
                var participants = await pickupPalGameRepository.ListParticipantsAsync(session.Id, cancellationToken);
                if (participants.Any(p => p.PlayerProfileId is null))
                {
                    return new PendingStatSubmissionModel(
                        Guid.Empty,
                        "Submit your latest stats",
                        $"Find yourself on {session.Title} to add your goals and assists",
                        IsPendingConfirmation: false,
                        SessionId: session.Id,
                        RequiresClaim: true);
                }

                continue;
            }

            var events = await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken);
            var mine = events.Where(x => x.SubmittedByPlayerProfileId == actor.Id).ToArray();
            if (mine.Any(x => x.ReviewStatus == MatchEventReviewStatus.Approved))
            {
                // Already confirmed - a change from here is a stat correction, not a submission.
                continue;
            }

            var isPending = mine.Any(x => x.ReviewStatus == MatchEventReviewStatus.Pending);
            return new PendingStatSubmissionModel(
                match.Id,
                isPending ? "Stats submitted" : "Submit your latest stats",
                isPending
                    ? $"Waiting on confirmation for {session.Title}"
                    : $"Add your goals and assists for {session.Title}",
                isPending,
                SessionId: session.Id);
        }

        return null;
    }
}

/// <summary>
/// Peer ratings, likes, and the MVP vote open once the game has been played and close a few days
/// later, so feedback reflects a game people still remember.
/// </summary>
public static class PeerFeedbackWindow
{
    public static readonly TimeSpan Duration = TimeSpan.FromDays(3);

    public static bool IsOpen(Session session, DateTime nowUtc) =>
        GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
        && nowUtc <= session.StartsAtUtc.Add(Duration);

    public static void Ensure(Session session, DateTime nowUtc)
    {
        if (!GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc))
        {
            throw new ApplicationConflictException("Rating opens after the game has been played.");
        }

        if (nowUtc > session.StartsAtUtc.Add(Duration))
        {
            throw new ApplicationConflictException(
                $"Rating closed {Duration.TotalDays:0} days after kick-off.");
        }
    }
}

/// <summary>Shared tally projection so goals and assists are counted the same way everywhere.</summary>
internal static class MatchStatsProjection
{
    internal static int CountGoals(IReadOnlyList<MatchEvent> events, Guid playerProfileId) =>
        events.Count(x => x.EventType == MatchEventType.Goal && x.PlayerProfileId == playerProfileId);

    internal static int CountAssists(IReadOnlyList<MatchEvent> events, Guid playerProfileId) =>
        events.Count(x => x.AssistPlayerProfileId == playerProfileId);

    internal static string DescribeTally(int goals, int assists)
    {
        var parts = new List<string>(2);
        if (goals > 0)
        {
            parts.Add($"{goals} {(goals == 1 ? "goal" : "goals")}");
        }

        if (assists > 0)
        {
            parts.Add($"{assists} {(assists == 1 ? "assist" : "assists")}");
        }

        return string.Join(" - ", parts);
    }
}
