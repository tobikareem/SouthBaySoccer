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
    bool CanSubmitOwnStats);

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

    public async Task<GameDayContextModel?> HandleAsync(CancellationToken cancellationToken = default)
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
        var session = SelectSession(
            confirmedCandidates.Length > 0 ? confirmedCandidates : candidates,
            clock.UtcNow)!;
        var attendance = attendanceBySessionId[session.Id];
        var eligibility = attendance.IsCurrentPlayerGoing
            ? await eligibilityService.CheckAsync(profile.Id, session.Id, cancellationToken)
            : new PlayerSessionEligibilityResult(false, attendance.IsCurrentPlayerWaitlisted
                ? "You are currently waitlisted for this session."
                : "A confirmed Going spot is required to check in.");
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
        var isDraftWindow = session.Status == SessionStatus.Published
            && nowUtc >= session.CheckInOpensAtUtc
            && !GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc);
        var canAssignCaptains = GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
            && isDraftWindow
            && (match is null || match.Status == MatchStatus.Draft);
        var canDraftTeam = match?.Status == MatchStatus.Draft
            && isDraftWindow
            && (GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
                || teams.Any(team => team.CaptainPlayerProfileId == profile.Id));
        var canApprovePostGame = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.Draft
                and not MatchStatus.NeedsReview
                and not MatchStatus.Published
                and not MatchStatus.Locked
            && (GameDayWorkflowAuthorization.IsGameAdmin(currentUser)
                || teams.Any(team => team.CaptainPlayerProfileId == profile.Id));
        // STAT-7/STAT-8 entry point: once teams are locked and the post-game window is open, a
        // player who was actually drafted can report their own tally and rate the side they played
        // with. Published/locked matches are settled and only move through a stat correction.
        var canSubmitOwnStats = match is not null
            && GameDayWorkflowQueries.IsPostGameOpen(session, nowUtc)
            && match.Status is not MatchStatus.Draft
                and not MatchStatus.Published
                and not MatchStatus.Locked
            && (await statsRepository.ListAssignmentsAsync(match.Id, cancellationToken))
                .Any(assignment => assignment.PlayerProfileId == profile.Id);
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
            canSubmitOwnStats);
    }

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

        if (!attendance.IsCurrentPlayerGoing || !eligibility.IsEligible)
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
