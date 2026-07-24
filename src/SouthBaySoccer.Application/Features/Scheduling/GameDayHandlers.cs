using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record GameDayPlayerModel(Guid PlayerProfileId, string DisplayName, bool IsGuest);

public sealed record GameDayRosterEntryModel(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    bool IsCheckedIn);

public sealed record GameDayContextModel(
    Guid SessionId,
    Guid MatchId,
    string Venue,
    DateTime StartsAtUtc,
    DateTime CheckInOpensAtUtc,
    DateTime CheckInClosesAtUtc,
    string Status,
    string StatusLabel,
    bool IsSelfCheckInAvailable,
    string PrimaryActionText,
    string? BlockReason,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerCheckedIn,
    int GoingCount,
    int CheckedInCount,
    int LateCount,
    bool CanAssignCaptains,
    bool CanDraftTeam,
    bool CanApprovePostGame,
    bool CanLateCheckIn,
    IReadOnlyList<GameDayPlayerModel> LateCheckInPlayers,
    IReadOnlyList<GameDayRosterEntryModel> Roster,
    bool CanManageCheckIns,
    bool CanSubmitOwnStats,
    IReadOnlyList<GameDayOptionModel> TodaysGames);

/// <summary>
/// One of today's games the player can act on, used to build the Game Day picker when more than one
/// runs the same day. <see cref="IsSelected"/> marks the one whose context is currently loaded.
/// </summary>
public sealed record GameDayOptionModel(
    Guid SessionId,
    string Title,
    string Venue,
    DateTime StartsAtUtc,
    string StatusLabel,
    bool IsSelected);

public sealed record RecentGameModel(
    Guid SessionId,
    Guid MatchId,
    string Title,
    string Venue,
    DateTime StartsAtUtc,
    string MatchStatus,
    int TeamCount,
    int PendingApprovalCount,
    bool CanEditTeams);

/// <summary>
/// Lists games that have already kicked off inside the admin edit window, so a game admin can go
/// back into a past session to fix teams or clear the stat queue. Game Day itself only ever shows
/// today, which otherwise leaves yesterday's match unreachable.
/// </summary>
public sealed class GetRecentGamesQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository,
    IStatsRepository statsRepository)
{
    public async Task<IReadOnlyList<RecentGameModel>> HandleAsync(CancellationToken cancellationToken = default)
    {
        GameDayWorkflowAuthorization.EnsureGameAdmin(currentUser);
        var nowUtc = clock.UtcNow;
        var sessions = await sessionRepository.ListGameDayCandidatesAsync(
            nowUtc.Subtract(GameDayWorkflowQueries.AdminTeamEditWindow),
            nowUtc,
            cancellationToken);

        var games = new List<RecentGameModel>(sessions.Count);
        foreach (var session in sessions.OrderByDescending(x => x.StartsAtUtc))
        {
            var match = await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken);
            var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);
            var teams = match is null
                ? []
                : await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
            var pendingApprovals = 0;
            if (match is not null)
            {
                var events = await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken);
                pendingApprovals = events.Count(x => x.ReviewStatus == MatchEventReviewStatus.Pending);
            }

            games.Add(new RecentGameModel(
                session.Id,
                match?.Id ?? Guid.Empty,
                session.Title,
                venue?.Name ?? "Unknown venue",
                session.StartsAtUtc,
                match?.Status.ToString() ?? "NotStarted",
                teams.Count,
                pendingApprovals,
                GameDayWorkflowQueries.IsAdminTeamEditOpen(session, nowUtc)
                    && match?.Status is not MatchStatus.Published and not MatchStatus.Locked));
        }

        return games;
    }
}

public sealed class GetTodayGameDayContextQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IStatsRepository statsRepository,
    IPlayerSessionEligibilityService eligibilityService)
{
    private const string CanCheckInPlayersPolicy = "CanCheckInPlayers";

    public async Task<GameDayContextModel?> HandleAsync(
        Guid? requestedSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        var localToday = SessionAdminTimeZone.ToLocal(clock.UtcNow).Date;
        var dayStartsAtUtc = SessionAdminTimeZone.ToUtc(localToday, TimeSpan.Zero);
        var dayEndsAtUtc = SessionAdminTimeZone.ToUtc(localToday.AddDays(1), TimeSpan.Zero);
        var candidates = await sessionRepository.ListGameDayCandidatesAsync(
            dayStartsAtUtc,
            dayEndsAtUtc,
            cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var attendanceBySessionId = new Dictionary<Guid, GameDayAttendanceRecord>(candidates.Count);
        foreach (var candidate in candidates)
        {
            attendanceBySessionId[candidate.Id] = await rsvpRepository.GetGameDayAttendanceAsync(
                candidate.Id,
                profile.Id,
                cancellationToken);
        }

        var confirmedCandidates = candidates
            .Where(candidate => attendanceBySessionId[candidate.Id].IsCurrentPlayerGoing)
            .ToArray();
        // The player picks between the games they're Going to; if they're Going to none (e.g. an
        // admin running the day), the pool falls back to every game so they can still operate one.
        IReadOnlyList<Session> pool = confirmedCandidates.Length > 0 ? confirmedCandidates : candidates;
        // Honour an explicit pick from the picker, but only within the pool the player may view;
        // an unknown or out-of-pool id falls back to the automatic selection.
        var session = (requestedSessionId is { } requestedId
                ? pool.FirstOrDefault(candidate => candidate.Id == requestedId)
                : null)
            ?? SelectSession(pool, clock.UtcNow)!;
        var attendance = attendanceBySessionId[session.Id];
        // Both Going and Waitlist players may check in at the field - a waitlisted player who shows
        // up often fills a no-show's spot, so the waitlist no longer blocks self check-in.
        var eligibility = attendance.IsCurrentPlayerGoing || attendance.IsCurrentPlayerWaitlisted
            ? await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken)
            : new PlayerSessionEligibilityResult(false, "A Going or waitlist spot is required to check in.");
        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);

        var nowUtc = clock.UtcNow;
        var isWithinWindow = nowUtc >= session.CheckInOpensAtUtc && nowUtc <= session.CheckInClosesAtUtc;
        var status = ResolveStatus(attendance, eligibility, isWithinWindow, nowUtc, session);
        var canLateCheckIn = currentUser.HasPolicy(CanCheckInPlayersPolicy)
            && nowUtc > session.CheckInClosesAtUtc;
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken);
        var teams = match is null
            ? []
            : await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        // Game admins set teams up ahead of time, so their window opens at publish; captains still
        // wait for check-in. Both close when post-game opens.
        var isGameAdmin = GameDayWorkflowAuthorization.IsGameAdmin(currentUser);
        var isDraftWindow = GameDayWorkflowQueries.IsTeamSetupOpen(session, nowUtc, isGameAdmin);
        var canAssignCaptains = isGameAdmin
            && isDraftWindow
            && (match is null || match.Status == MatchStatus.Draft);
        // Admins may still rearrange a match that already has results; only Published/Locked are
        // settled. Captains keep editing only while the match is still a Draft.
        var canDraftTeam = isDraftWindow
            && match is not null
            && (isGameAdmin
                ? match.Status is not MatchStatus.Published and not MatchStatus.Locked
                : match.Status == MatchStatus.Draft
                    && teams.Any(team => team.CaptainPlayerProfileId == profile.Id));
        var canApprovePostGame = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.Draft
                and not MatchStatus.NeedsReview
                and not MatchStatus.Published
                and not MatchStatus.Locked
            && (GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
                || teams.Any(team => team.CaptainPlayerProfileId == profile.Id));
        var latePlayers = canLateCheckIn
            ? await ListConfirmedPlayersAsync(
                session.Id,
                attendance.CheckedInPlayerProfileIds,
                cancellationToken)
            : [];

        var canManageCheckIns = currentUser.HasPolicy(CanCheckInPlayersPolicy);
        var checkedInIds = attendance.CheckedInPlayerProfileIds.ToHashSet();
        var roster = (await GameDayWorkflowQueries.ListEligibleRosterAsync(
                rsvpRepository,
                pickupPalGameRepository,
                session.Id,
                cancellationToken))
            .Select(member => new GameDayRosterEntryModel(
                member.PlayerProfileId,
                member.DisplayName,
                member.IsGuest,
                member.WaitlistPosition is not null,
                checkedInIds.Contains(member.PlayerProfileId)))
            .ToArray();

        // STAT-7/STAT-8 entry point: once the game has been played, anyone who was on the confirmed
        // roster can report their own tally and rate the side they played with - being drafted onto
        // a team is not required, since a session may never have been drafted at all.
        // Published/locked matches are settled and only move through a stat correction.
        var canSubmitOwnStats = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.Published and not MatchStatus.Locked
            && roster.Any(member => member.PlayerProfileId == profile.Id);

        // The picker lists every game in the pool (ordered by kick-off), reusing the already-loaded
        // venue for the selected one and looking up the rest. Attendance is already fetched per game.
        var todaysGames = new List<GameDayOptionModel>(pool.Count);
        foreach (var candidate in pool.OrderBy(candidate => candidate.StartsAtUtc))
        {
            var candidateVenue = candidate.VenueId == session.VenueId
                ? venue
                : await venueRepository.GetByIdAsync(candidate.VenueId, cancellationToken);
            todaysGames.Add(new GameDayOptionModel(
                candidate.Id,
                candidate.Title,
                candidateVenue?.Name ?? "Unknown venue",
                candidate.StartsAtUtc,
                DescribeAttendance(attendanceBySessionId[candidate.Id]),
                candidate.Id == session.Id));
        }

        return new GameDayContextModel(
            session.Id,
            match?.Id ?? Guid.Empty,
            venue?.Name ?? "Unknown venue",
            session.StartsAtUtc,
            session.CheckInOpensAtUtc,
            session.CheckInClosesAtUtc,
            status.Status,
            status.Label,
            status.CanCheckIn,
            status.PrimaryAction,
            status.BlockReason,
            attendance.IsCurrentPlayerGoing,
            attendance.IsCurrentPlayerCheckedIn,
            attendance.GoingCount,
            attendance.CheckedInCount,
            attendance.LateCount,
            canAssignCaptains,
            canDraftTeam,
            canApprovePostGame,
            canLateCheckIn,
            latePlayers,
            roster,
            canManageCheckIns,
            canSubmitOwnStats,
            todaysGames);
    }

    private static string DescribeAttendance(GameDayAttendanceRecord attendance) =>
        attendance.IsCurrentPlayerCheckedIn ? "Checked in"
        : attendance.IsCurrentPlayerGoing ? "Going"
        : attendance.IsCurrentPlayerWaitlisted ? "Waitlist"
        : "Not going";

    private async Task<IReadOnlyList<GameDayPlayerModel>> ListConfirmedPlayersAsync(
        Guid sessionId,
        IReadOnlyList<Guid> checkedInPlayerProfileIds,
        CancellationToken cancellationToken)
    {
        var local = await rsvpRepository.ListGoingRosterAsync(sessionId, cancellationToken);
        var imported = await pickupPalGameRepository.ListParticipantsAsync(sessionId, cancellationToken);
        var localIds = local.Select(player => player.PlayerProfileId).ToHashSet();
        var checkedInIds = checkedInPlayerProfileIds.ToHashSet();

        return local
            .Where(player => !checkedInIds.Contains(player.PlayerProfileId))
            .Select(player => new GameDayPlayerModel(player.PlayerProfileId, player.DisplayName, player.IsGuest))
            .Concat(imported
                .Where(player => !player.IsWaitlist
                    && player.PlayerProfileId is { } playerId
                    && !localIds.Contains(playerId)
                    && !checkedInIds.Contains(playerId))
                .Select(player => new GameDayPlayerModel(
                    player.PlayerProfileId!.Value,
                    player.DisplayName,
                    player.IsGuest)))
            .OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Session? SelectSession(IReadOnlyList<Session> candidates, DateTime nowUtc) =>
        candidates
            .OrderBy(session => nowUtc >= session.CheckInOpensAtUtc && nowUtc <= session.CheckInClosesAtUtc ? 0
                : session.StartsAtUtc >= nowUtc ? 1 : 2)
            .ThenBy(session => session.StartsAtUtc >= nowUtc
                ? session.StartsAtUtc.Ticks
                : -session.StartsAtUtc.Ticks)
            .ThenBy(session => session.Id)
            .FirstOrDefault();

    private static GameDayStatusProjection ResolveStatus(
        GameDayAttendanceRecord attendance,
        PlayerSessionEligibilityResult eligibility,
        bool isWithinWindow,
        DateTime nowUtc,
        Session session)
    {
        if (attendance.IsCurrentPlayerCheckedIn)
        {
            return new("CheckedIn", "Checked in", false, "Checked in", null);
        }

        if ((!attendance.IsCurrentPlayerGoing && !attendance.IsCurrentPlayerWaitlisted) || !eligibility.IsEligible)
        {
            return new("Blocked", "Blocked", false, "Check-in unavailable", eligibility.Reason);
        }

        if (isWithinWindow)
        {
            return new("Open", "Open", true, "Check in at field", null);
        }

        return nowUtc < session.CheckInOpensAtUtc
            ? new("Closed", "Not open", false, "Check-in opens later", "Check-in has not opened yet.")
            : new("Closed", "Closed", false, "GameAdmin override required", "Check-in is closed. Ask a GameAdmin to record a late arrival.");
    }

    private sealed record GameDayStatusProjection(
        string Status,
        string Label,
        bool CanCheckIn,
        string PrimaryAction,
        string? BlockReason);
}
