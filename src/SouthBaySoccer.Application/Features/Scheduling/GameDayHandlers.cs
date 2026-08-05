using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

public sealed record GameDayPlayerModel(Guid PlayerProfileId, string DisplayName, bool IsGuest);

/// <summary>
/// One person on the Game Day roster as displayed. Imported Pickup Pal participants who were never
/// matched to a profile are included: they occupy a real place on the roster, so leaving them out
/// made the tile counts disagree with the session card (a 26-strong waitlist reading as 11).
/// </summary>
/// <param name="PlayerProfileId">
/// Null for an imported participant with no profile behind them. Such a member is display-only —
/// they cannot be checked in or made captain until someone links them, because every one of those
/// actions is keyed on a profile id.
/// </param>
/// <param name="PickupPalParticipantId">
/// Set only when <paramref name="PlayerProfileId"/> is null; identifies the unlinked participant so
/// the client can hand it to the matching flow.
/// </param>
public sealed record GameDayRosterEntryModel(
    Guid? PlayerProfileId,
    string DisplayName,
    bool IsGuest,
    bool IsWaitlist,
    bool IsCheckedIn,
    string? PickupPalParticipantId = null);

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
    IReadOnlyList<GameDayOptionModel> TodaysGames,
    bool CanViewTeams = false,
    string Title = "",
    string? GroupName = null,
    bool IsSpectator = false,
    bool CanJoin = false,
    string? JoinBlockedReason = null,
    int Capacity = 0,
    bool CanShowAllGames = false,
    bool IsShowingAllGames = false);

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

        var venuesById = (await venueRepository.ListByIdsAsync(
                sessions.Select(session => session.VenueId).Distinct().ToArray(),
                cancellationToken))
            .ToDictionary(venue => venue.Id);

        var games = new List<RecentGameModel>(sessions.Count);
        foreach (var session in sessions.OrderByDescending(x => x.StartsAtUtc))
        {
            var match = await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken);
            var venue = venuesById.GetValueOrDefault(session.VenueId);
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

/// <summary>One player on a last-game team sheet, with approved goal and assist tallies.</summary>
public sealed record LastGameTeamMemberModel(
    Guid PlayerProfileId,
    string DisplayName,
    bool IsCaptain,
    int Goals,
    int Assists);

/// <summary>One team on the last game, named by its captain, with its settled result when published.</summary>
public sealed record LastGameTeamModel(
    Guid TeamId,
    string Name,
    string CaptainName,
    string ResultLabel,
    IReadOnlyList<LastGameTeamMemberModel> Members);

/// <summary>
/// A player-facing summary of the player's most recent game, shown on Game Day when there is no
/// relevant game today: counts, the team sheets (captains + members + approved goal tallies), and
/// the follow-up actions the viewer may still take on it (lock teams, match players, confirm the
/// result and stats).
/// </summary>
public sealed record LastGameSummaryModel(
    Guid SessionId,
    string Title,
    string? GroupName,
    string Venue,
    DateTime StartsAtUtc,
    int GoingCount,
    int CheckedInCount,
    int TeamCount,
    string? ResultSummary,
    int WaitlistCount = 0,
    IReadOnlyList<LastGameTeamModel>? Teams = null,
    bool CanLockTeams = false,
    bool CanMatchPlayers = false,
    bool CanApprovePostGame = false,
    Guid MatchId = default,
    bool CanRateTeammates = false);

/// <summary>
/// Finds the player's most recent past game inside a 30-day window: preferably one they held a spot
/// on (Going/Waitlisted/Checked in), else the latest game run by a WhatsApp group they belong to.
/// Null when neither exists — other groups' games are never surfaced.
/// </summary>
public interface ILastGameSummaryQueryHandler
{
    Task<LastGameSummaryModel?> HandleAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LastGameSummaryModel>> HandleRecentAsync(
        int take = 3,
        CancellationToken cancellationToken = default);
}

/// <summary>Default implementation of the player-facing last-game summary queries.</summary>
public sealed class GetLastGameSummaryQueryHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IVenueRepository venueRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGameRepository pickupPalGameRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IStatsRepository statsRepository) : ILastGameSummaryQueryHandler
{
    /// <summary>How far back to look for the player's most recent game.</summary>
    public static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Upper bound on past sessions examined. The page is global (newest first) and the relevance
    /// filter runs in memory, so the cap must comfortably exceed 30 days of sessions across every
    /// imported group or the effective window silently shrinks; ~5 groups at 3 games/week fit. If
    /// group volume outgrows this, push the attendance/group filter into SQL instead of raising it
    /// again (noted in the GDAY-2 design).
    /// </summary>
    private const int MaxPastSessions = 60;

    public async Task<LastGameSummaryModel?> HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        // Window ends at today's venue-local midnight so today's own (not-yet-relevant) games never
        // masquerade as "the last game".
        var localToday = SessionAdminTimeZone.ToLocal(clock.UtcNow).Date;
        var todayStartsAtUtc = SessionAdminTimeZone.ToUtc(localToday, TimeSpan.Zero);
        var sessions = await sessionRepository.ListPastGameDayCandidatesAsync(
            todayStartsAtUtc.Subtract(LookbackWindow),
            todayStartsAtUtc,
            MaxPastSessions,
            cancellationToken);
        if (sessions.Count == 0)
        {
            return null;
        }

        var attendanceBySessionId = await rsvpRepository.GetGameDayAttendanceBatchAsync(
            sessions.Select(session => session.Id).ToArray(),
            profile.Id,
            cancellationToken);

        // Sessions arrive newest-first, so the first hit in each tier is the most recent one.
        var session = sessions.FirstOrDefault(candidate =>
            attendanceBySessionId[candidate.Id].IsCurrentPlayerGoing
            || attendanceBySessionId[candidate.Id].IsCurrentPlayerWaitlisted
            || attendanceBySessionId[candidate.Id].IsCurrentPlayerCheckedIn);
        var groupNamesBySessionId = await sessionRepository.GetGroupNamesBySessionAsync(
            sessions.Select(candidate => candidate.Id).ToArray(),
            cancellationToken);
        if (session is null)
        {
            var groups = await playerGroupLinkRepository.ListPlayerGroupsAsync(profile.Id, cancellationToken);
            var groupNames = groups
                .Select(group => group.GroupName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            session = sessions.FirstOrDefault(candidate =>
                groupNamesBySessionId.TryGetValue(candidate.Id, out var groupName)
                && groupNames.Contains(groupName.Trim()));
        }

        if (session is null)
        {
            return null;
        }

        return await BuildSummaryAsync(
            profile,
            session,
            attendanceBySessionId[session.Id],
            groupNamesBySessionId,
            cancellationToken);
    }

    /// <summary>
    /// Returns the player's newest attended games. Unlike the single-summary fallback, group
    /// membership alone never qualifies a game for this history.
    /// </summary>
    public async Task<IReadOnlyList<LastGameSummaryModel>> HandleRecentAsync(
        int take = 3,
        CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var localToday = SessionAdminTimeZone.ToLocal(clock.UtcNow).Date;
        var todayStartsAtUtc = SessionAdminTimeZone.ToUtc(localToday, TimeSpan.Zero);
        var sessions = await sessionRepository.ListPastGameDayCandidatesAsync(
            todayStartsAtUtc.Subtract(LookbackWindow),
            todayStartsAtUtc,
            MaxPastSessions,
            cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        var sessionIds = sessions.Select(session => session.Id).ToArray();
        var attendanceBySessionId = await rsvpRepository.GetGameDayAttendanceBatchAsync(
            sessionIds,
            profile.Id,
            cancellationToken);
        var groupNamesBySessionId = await sessionRepository.GetGroupNamesBySessionAsync(
            sessionIds,
            cancellationToken);
        var attendedSessions = sessions
            // Recent history is an attendance record, not an RSVP-intent record. Going-only
            // no-shows and waitlisted players who never received a spot must not see team details.
            .Where(session => attendanceBySessionId[session.Id].IsCurrentPlayerCheckedIn)
            .Take(Math.Clamp(take, 1, 3))
            .ToArray();
        var venuesById = (await venueRepository.ListByIdsAsync(
                attendedSessions.Select(session => session.VenueId).Distinct().ToArray(),
                cancellationToken))
            .ToDictionary(venue => venue.Id);
        var statsBySessionId = (await statsRepository.ListGameDaySummaryStatsAsync(
                attendedSessions.Select(session => session.Id).ToArray(),
                cancellationToken))
            .ToDictionary(summary => summary.SessionId);
        var summaries = new List<LastGameSummaryModel>(attendedSessions.Length);
        foreach (var session in attendedSessions)
        {
            summaries.Add(await BuildSummaryAsync(
                profile,
                session,
                attendanceBySessionId[session.Id],
                groupNamesBySessionId,
                cancellationToken,
                new PreloadedSummaryData(
                    venuesById.GetValueOrDefault(session.VenueId),
                    statsBySessionId.GetValueOrDefault(session.Id))));
        }

        return summaries;
    }

    private async Task<LastGameSummaryModel> BuildSummaryAsync(
        PlayerProfile profile,
        Session session,
        GameDayAttendanceRecord attendance,
        IReadOnlyDictionary<Guid, string> groupNamesBySessionId,
        CancellationToken cancellationToken,
        PreloadedSummaryData? preloaded = null)
    {
        var venue = preloaded is null
            ? await venueRepository.GetByIdAsync(session.VenueId, cancellationToken)
            : preloaded.Venue;
        var match = preloaded is null
            ? await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken)
            : preloaded.Stats?.Match;
        var teams = preloaded?.Stats?.Teams
            ?? (match is null
                ? []
                : await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken));
        var results = preloaded?.Stats?.Results
            ?? (match is null
                ? []
                : await statsRepository.ListMatchResultsAsync(match.Id, cancellationToken));
        var isSettled = match?.Status is MatchStatus.Published or MatchStatus.Locked;
        var resultSummary = isSettled ? BuildResultSummary(results, teams) : null;

        // The display roster gives counts consistent with the Game Day tiles (waitlist included)
        // and the names the team sheets and goal tallies resolve against.
        var checkedInIds = attendance.CheckedInPlayerProfileIds.ToHashSet();
        var roster = await GameDayWorkflowQueries.ListDisplayRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            session.Id,
            checkedInIds,
            cancellationToken);
        var assignments = preloaded?.Stats?.Assignments
            ?? (match is null
                ? []
                : await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken));
        var teamModels = match is null || teams.Count == 0
            ? []
            : await BuildTeamModelsAsync(
                match,
                teams,
                assignments,
                results,
                roster,
                isSettled,
                cancellationToken,
                preloaded?.Stats?.Events);

        // Follow-up actions: the game may still need its teams locked, imported names matched, or
        // its result/goals confirmed — surfacing them here saves the admin a trip through Recent
        // games. Windows and roles mirror the live Game Day rules exactly.
        var nowUtc = clock.UtcNow;
        var isGameAdmin = GameDayWorkflowAuthorization.IsGameAdmin(currentUser);
        var isCaptain = teams.Any(team => team.CaptainPlayerProfileId == profile.Id);
        var canLockTeams = isGameAdmin
            && GameDayWorkflowQueries.IsAdminTeamEditOpen(session, nowUtc)
            && (match is null || match.Status == MatchStatus.Draft);
        var canMatchPlayers = isGameAdmin
            && roster.Any(member => member.PlayerProfileId is null);
        // Mirrors the post-game screen's own gate: captains and admins confirm once teams are
        // locked, and an admin can also start from a Draft game whose teams are lockable — the
        // screen auto-locks on the first recorded result.
        var teamsLockable = teams.Count is >= 2 and <= 4
            && teams.All(team => team.CaptainPlayerProfileId is { } captainId
                && assignments.Any(assignment => assignment.MatchTeamId == team.Id
                    && assignment.PlayerProfileId == captainId));
        var canApprovePostGame = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.NeedsReview
                and not MatchStatus.Published
                and not MatchStatus.Locked
            && (match.Status == MatchStatus.Draft
                ? isGameAdmin && teamsLockable
                : isGameAdmin || isCaptain);
        var canRateTeammates = match is not null
            && PeerFeedbackWindow.IsOpen(session, nowUtc)
            && roster.Any(member => member.PlayerProfileId == profile.Id);

        return new LastGameSummaryModel(
            session.Id,
            session.Title,
            groupNamesBySessionId.GetValueOrDefault(session.Id),
            venue?.Name ?? "Unknown venue",
            session.StartsAtUtc,
            roster.Count(member => !member.IsWaitlist),
            roster.Count(member => member.IsCheckedIn),
            teams.Count,
            resultSummary,
            roster.Count(member => member.IsWaitlist),
            teamModels,
            canLockTeams,
            canMatchPlayers,
            canApprovePostGame,
            match?.Id ?? Guid.Empty,
            canRateTeammates);
    }

    private sealed record PreloadedSummaryData(Venue? Venue, GameDaySummaryStatsRecord? Stats);

    /// <summary>
    /// Team sheets with approved goal tallies. Only approved Goal events count — pending or rejected
    /// submissions belong to the confirmation flow, not a summary presented as fact.
    /// </summary>
    private async Task<IReadOnlyList<LastGameTeamModel>> BuildTeamModelsAsync(
        Match match,
        IReadOnlyList<MatchTeam> teams,
        IReadOnlyList<TeamAssignment> assignments,
        IReadOnlyList<MatchResult> results,
        IReadOnlyList<GameDayRosterEntryModel> roster,
        bool isSettled,
        CancellationToken cancellationToken,
        IReadOnlyList<MatchEvent>? preloadedEvents = null)
    {
        var events = preloadedEvents
            ?? await statsRepository.ListMatchEventsAsync(match.Id, cancellationToken);
        var goalsByPlayerId = events
            .Where(matchEvent => matchEvent.EventType == MatchEventType.Goal
                && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                && matchEvent.PlayerProfileId is not null)
            .GroupBy(matchEvent => matchEvent.PlayerProfileId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var assistsByPlayerId = events
            .Where(matchEvent => matchEvent.EventType == MatchEventType.Goal
                && matchEvent.ReviewStatus == MatchEventReviewStatus.Approved
                && matchEvent.AssistPlayerProfileId is not null)
            .GroupBy(matchEvent => matchEvent.AssistPlayerProfileId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        var namesByPlayerId = roster
            .Where(member => member.PlayerProfileId is not null)
            .GroupBy(member => member.PlayerProfileId!.Value)
            .ToDictionary(group => group.Key, group => group.First().DisplayName);
        var resultsByTeamId = results.ToDictionary(result => result.MatchTeamId);

        return teams
            .OrderBy(team => team.TeamNumber)
            .Select(team =>
            {
                var members = assignments
                    .Where(assignment => assignment.MatchTeamId == team.Id)
                    .Select(assignment => new LastGameTeamMemberModel(
                        assignment.PlayerProfileId,
                        namesByPlayerId.GetValueOrDefault(assignment.PlayerProfileId, "Unknown player"),
                        assignment.PlayerProfileId == team.CaptainPlayerProfileId,
                        goalsByPlayerId.GetValueOrDefault(assignment.PlayerProfileId),
                        assistsByPlayerId.GetValueOrDefault(assignment.PlayerProfileId)))
                    // Captain first, then scorers, then alphabetical — the order a recap is read in.
                    .OrderByDescending(member => member.IsCaptain)
                    .ThenByDescending(member => member.Goals)
                    .ThenBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new LastGameTeamModel(
                    team.Id,
                    team.Name,
                    namesByPlayerId.GetValueOrDefault(
                        team.CaptainPlayerProfileId ?? Guid.Empty,
                        "No captain"),
                    isSettled && resultsByTeamId.TryGetValue(team.Id, out var result)
                        ? BuildTallyLabel(result)
                        : string.Empty,
                    members);
            })
            .ToArray();
    }

    // "Team Vic 2W · Team Ade 1W 1D" — wins always shown so the summary reads as a result even at
    // 0, draws/losses only when they occurred.
    private static string? BuildResultSummary(
        IReadOnlyList<MatchResult> results,
        IReadOnlyList<MatchTeam> teams)
    {
        if (results.Count == 0)
        {
            return null;
        }

        var teamNamesById = teams.ToDictionary(team => team.Id, team => team.Name);
        var parts = results
            .OrderBy(result => teamNamesById.GetValueOrDefault(result.MatchTeamId, string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(result => $"{teamNamesById.GetValueOrDefault(result.MatchTeamId, "Team")} {BuildTallyLabel(result)}");
        return string.Join(" · ", parts);
    }

    private static string BuildTallyLabel(MatchResult result)
    {
        var tally = $"{result.Wins}W";
        if (result.Draws > 0)
        {
            tally += $" {result.Draws}D";
        }

        if (result.Losses > 0)
        {
            tally += $" {result.Losses}L";
        }

        return tally;
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
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IStatsRepository statsRepository,
    IPlayerSessionEligibilityService eligibilityService)
{
    private const string CanCheckInPlayersPolicy = "CanCheckInPlayers";

    public async Task<GameDayContextModel?> HandleAsync(
        Guid? requestedSessionId = null,
        bool showAllGames = false,
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

        var attendanceBySessionId = await rsvpRepository.GetGameDayAttendanceBatchAsync(
            candidates.Select(candidate => candidate.Id).ToArray(),
            profile.Id,
            cancellationToken);

        // A player's day is only the games they hold a spot on (Going or Waitlisted). With none,
        // the fallback is games run by a WhatsApp group they belong to — as a spectator — and every
        // other game stays hidden. Game admins can opt into the full list; the old always-show-all
        // fallback made everyone's Game Day as busy as an admin's.
        var isGameAdmin = GameDayWorkflowAuthorization.IsGameAdmin(currentUser);
        var isShowingAll = showAllGames && isGameAdmin;
        var attendingCandidates = candidates
            .Where(candidate => attendanceBySessionId[candidate.Id].IsCurrentPlayerGoing
                || attendanceBySessionId[candidate.Id].IsCurrentPlayerWaitlisted)
            .ToArray();
        var groupNamesBySessionId = await sessionRepository.GetGroupNamesBySessionAsync(
            candidates.Select(candidate => candidate.Id).ToArray(),
            cancellationToken);
        IReadOnlyList<Session> pool;
        if (isShowingAll)
        {
            pool = candidates;
        }
        else if (attendingCandidates.Length > 0)
        {
            pool = attendingCandidates;
        }
        else
        {
            pool = await ListGroupPoolAsync(profile.Id, candidates, groupNamesBySessionId, cancellationToken);
            if (pool.Count == 0)
            {
                return null;
            }
        }

        // Honour an explicit pick from the picker, but only within the pool the player may view;
        // an unknown or out-of-pool id falls back to the automatic selection.
        var session = (requestedSessionId is { } requestedId
                ? pool.FirstOrDefault(candidate => candidate.Id == requestedId)
                : null)
            ?? SelectSession(pool, clock.UtcNow)!;
        var attendance = attendanceBySessionId[session.Id];
        // Spectator: viewing a group's game without holding a spot on it. Everything on the page is
        // read-only except one Join action; an admin who explicitly widened to all games is running
        // the day, not spectating.
        var isSpectator = !isShowingAll
            && !attendance.IsCurrentPlayerGoing
            && !attendance.IsCurrentPlayerWaitlisted;
        // Both Going and Waitlist players may check in at the field - a waitlisted player who shows
        // up often fills a no-show's spot, so the waitlist no longer blocks self check-in.
        var eligibility = attendance.IsCurrentPlayerGoing || attendance.IsCurrentPlayerWaitlisted
            ? await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken)
            : new PlayerSessionEligibilityResult(false, "A Going or waitlist spot is required to check in.");
        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);

        var nowUtc = clock.UtcNow;
        var isWithinWindow = nowUtc >= session.CheckInOpensAtUtc && nowUtc <= session.CheckInClosesAtUtc;
        // A spectator never sees the check-in machinery, so the eligibility reason would only
        // confuse; the client renders the spectator banner and Join action off IsSpectator instead.
        var status = isSpectator
            ? new GameDayStatusProjection("Blocked", "Spectator", false, "Join this game", null)
            : ResolveStatus(attendance, eligibility, isWithinWindow, nowUtc, session);
        var canLateCheckIn = currentUser.HasPolicy(CanCheckInPlayersPolicy)
            && nowUtc > session.CheckInClosesAtUtc;
        var match = await statsRepository.FindPrimaryMatchBySessionAsync(session.Id, cancellationToken);
        var teams = match is null
            ? []
            : await statsRepository.ListMatchTeamsAsync(match.Id, cancellationToken);
        // Game admins set teams up ahead of time, so their window opens at publish; captains act
        // all of the session's game day. Both close when post-game opens.
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
        var roster = await GameDayWorkflowQueries.ListDisplayRosterAsync(
            rsvpRepository,
            pickupPalGameRepository,
            playerProfileRepository,
            session.Id,
            checkedInIds,
            cancellationToken);

        // STAT-7/STAT-8 entry point: once the game has been played, anyone who was on the confirmed
        // roster can report their own tally and rate the side they played with - being drafted onto
        // a team is not required, since a session may never have been drafted at all.
        // Published/locked matches are settled and only move through a stat correction.
        var canSubmitOwnStats = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.Published and not MatchStatus.Locked
            && roster.Any(member => member.PlayerProfileId == profile.Id);

        // Any rostered player may view the teams read-only — including mid-draft, where the teams
        // view now labels the state explicitly (whose turn it is, who is yet to be picked) so a
        // partial sheet reads as "draft in progress", never as a settled roster. The client shows
        // this only to players who cannot draft (captains/admins use the draft screen instead).
        var canViewTeams = match is not null
            && teams.Count > 0
            && roster.Any(member => member.PlayerProfileId == profile.Id);

        // Spectators are strictly read-only: every profile-keyed action is withheld even where the
        // flags above would technically allow it (e.g. an admin browsing a group game without the
        // all-games toggle). Join is the one action offered, and only while RSVP is open — the
        // waiver/payment gates still run when the RSVP is actually submitted.
        var canJoin = false;
        string? joinBlockedReason = null;
        if (isSpectator)
        {
            canAssignCaptains = false;
            canDraftTeam = false;
            canApprovePostGame = false;
            canLateCheckIn = false;
            latePlayers = [];
            canManageCheckIns = false;
            canSubmitOwnStats = false;
            canViewTeams = false;
            canJoin = session.Status == SessionStatus.Published && nowUtc < session.RsvpDeadlineUtc;
            joinBlockedReason = canJoin ? null : "RSVP is closed for this game.";
        }

        // The picker lists every game in the pool (ordered by kick-off). Venues load in one batched
        // query rather than one per game; attendance is already fetched for every candidate.
        var poolVenuesById = (await venueRepository.ListByIdsAsync(
                pool.Select(candidate => candidate.VenueId).Distinct().ToArray(),
                cancellationToken))
            .ToDictionary(poolVenue => poolVenue.Id);
        var todaysGames = pool
            .OrderBy(candidate => candidate.StartsAtUtc)
            .Select(candidate => new GameDayOptionModel(
                candidate.Id,
                candidate.Title,
                (candidate.VenueId == session.VenueId
                    ? venue
                    : poolVenuesById.GetValueOrDefault(candidate.VenueId))?.Name ?? "Unknown venue",
                candidate.StartsAtUtc,
                DescribeAttendance(attendanceBySessionId[candidate.Id]),
                candidate.Id == session.Id))
            .ToList();

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
            todaysGames,
            canViewTeams,
            session.Title,
            groupNamesBySessionId.GetValueOrDefault(session.Id),
            isSpectator,
            canJoin,
            joinBlockedReason,
            session.Capacity,
            CanShowAllGames: isGameAdmin,
            IsShowingAllGames: isShowingAll);
    }

    /// <summary>
    /// The spectator pool: today's games run by a WhatsApp group the player belongs to, matched by
    /// group name (trimmed, case-insensitive) because group ids are deliberately never stored.
    /// Sessions created by hand carry no group and are never in this pool.
    /// </summary>
    private async Task<IReadOnlyList<Session>> ListGroupPoolAsync(
        Guid playerProfileId,
        IReadOnlyList<Session> candidates,
        IReadOnlyDictionary<Guid, string> groupNamesBySessionId,
        CancellationToken cancellationToken)
    {
        if (groupNamesBySessionId.Count == 0)
        {
            return [];
        }

        var groups = await playerGroupLinkRepository.ListPlayerGroupsAsync(playerProfileId, cancellationToken);
        if (groups.Count == 0)
        {
            return [];
        }

        var groupNames = groups
            .Select(group => group.GroupName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(candidate => groupNamesBySessionId.TryGetValue(candidate.Id, out var groupName)
                && groupNames.Contains(groupName.Trim()))
            .ToArray();
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
