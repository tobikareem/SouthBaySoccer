using System.Globalization;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Rosters;
using SouthBaySoccer.Contracts.Sessions;
using SouthBaySoccer.Contracts.Stats;

namespace SouthBaySoccer.SeedData;

public sealed class SeedState
{
    private readonly Lock syncRoot = new();
    private Dictionary<Guid, RosterDto> rosters = [];
    private MatchStatsDto matchStats = SeedFixtures.MatchStats;
    private IReadOnlyList<RateableTeammateDto> rateableTeammates = [];

    // ADMIN-4: drafts created on this device and the sessions published from them. Published sessions
    // are surfaced in the dashboard "Coming up" feed and openable for RSVP, all cleared by Reset().
    private Dictionary<Guid, CreateSessionCommand> drafts = [];
    private Dictionary<Guid, Guid> publishedSessionByDraft = [];
    private Dictionary<Guid, SessionSummaryDto> publishedSummaries = [];
    private Dictionary<Guid, SessionDetailDto> publishedDetails = [];
    private Dictionary<Guid, CreateSessionCommand> managedSessionCommands = [];
    private Dictionary<Guid, SessionSummaryDto> managedSummaryOverrides = [];
    private Dictionary<Guid, SessionDetailDto> managedDetailOverrides = [];

    public SeedState()
    {
        Reset();
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            rosters = SeedFixtures.Rosters.ToDictionary(
                pair => pair.Key,
                pair => CopyRoster(pair.Value));
            matchStats = CopyMatchStats(SeedFixtures.MatchStats);
            rateableTeammates = CopyRateableTeammates(SeedFixtures.RateableTeammates);
            drafts = [];
            publishedSessionByDraft = [];
            publishedSummaries = [];
            publishedDetails = [];
            managedSummaryOverrides = [];
            managedDetailOverrides = [];
            managedSessionCommands = SeedFixtures.Sessions.Values
                .ToDictionary(session => session.Id, ToEditableCommand);
        }
    }

    public SessionsDashboardDto GetDashboard()
    {
        lock (syncRoot)
        {
            var featuredSource = ResolveSummary(SeedFixtures.Dashboard.FeaturedSession);
            var featured = ApplyRosterState(featuredSource);
            var comingUp = SeedFixtures.Dashboard.ComingUpSessions
                .Select(ResolveSummary)
                .Concat(publishedSummaries.Values)
                .Select(ApplyRosterState)
                .ToArray();

            return SeedFixtures.Dashboard with
            {
                FeaturedSession = featured,
                ComingUpSessions = Array.AsReadOnly(comingUp)
            };
        }
    }

    public SessionDetailDto? GetSession(Guid sessionId)
    {
        lock (syncRoot)
        {
            if (!managedDetailOverrides.TryGetValue(sessionId, out var session)
                && !SeedFixtures.Sessions.TryGetValue(sessionId, out session)
                && !publishedDetails.TryGetValue(sessionId, out session))
            {
                return null;
            }

            var roster = rosters[sessionId];
            return session with
            {
                GoingCount = roster.Going.Count,
                IsFull = roster.Going.Count >= session.Capacity,
                IsRsvpAvailable = roster.Going.Count < session.Capacity,
                IsGoing = roster.Going.Any(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId)
            };
        }
    }

    public RosterDto? GetRoster(Guid sessionId)
    {
        lock (syncRoot)
        {
            return rosters.TryGetValue(sessionId, out var roster)
                ? CopyRoster(roster)
                : null;
        }
    }

    public ClientCommandResult SetRsvpIntent(Guid sessionId, bool isGoing)
    {
        lock (syncRoot)
        {
            if (!rosters.TryGetValue(sessionId, out var roster)
                || !TryGetCapacity(sessionId, out var capacity))
            {
                return ClientCommandResult.Failure("session_not_found", "The session was not found.");
            }

            var going = roster.Going.ToList();
            var waitlist = roster.Waitlist.ToList();
            var existingGoing = going.FindIndex(
                entry => entry.Player.Id == SeedFixtures.CurrentPlayerId);

            if (isGoing)
            {
                if (existingGoing >= 0)
                {
                    return ClientCommandResult.Success;
                }

                if (going.Count >= capacity)
                {
                    return ClientCommandResult.Failure(
                        "session_full",
                        "The session is full. Join the waitlist instead.");
                }

                waitlist.RemoveAll(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId);
                going.Add(new RosterEntryDto(SeedFixtures.Players[0], true));
            }
            else if (existingGoing >= 0)
            {
                going.RemoveAt(existingGoing);
            }

            rosters[sessionId] = new RosterDto(
                sessionId,
                Array.AsReadOnly(going.ToArray()),
                ReorderWaitlist(waitlist));

            return ClientCommandResult.Success;
        }
    }

    public ClientCommandResult JoinWaitlist(Guid sessionId)
    {
        lock (syncRoot)
        {
            if (!rosters.TryGetValue(sessionId, out var roster)
                || !TryGetCapacity(sessionId, out var capacity))
            {
                return ClientCommandResult.Failure("session_not_found", "The session was not found.");
            }

            if (roster.Going.Count < capacity)
            {
                return ClientCommandResult.Failure(
                    "session_not_full",
                    "The session still has space; record a going RSVP instead.");
            }

            if (roster.Going.Any(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId)
                || roster.Waitlist.Any(entry => entry.Player.Id == SeedFixtures.CurrentPlayerId))
            {
                return ClientCommandResult.Success;
            }

            var waitlist = roster.Waitlist.ToList();
            waitlist.Add(new WaitlistEntryDto(SeedFixtures.Players[0], waitlist.Count + 1));
            rosters[sessionId] = roster with { Waitlist = ReorderWaitlist(waitlist) };

            return ClientCommandResult.Success;
        }
    }

    public MatchStatsDto GetMatchStats()
    {
        lock (syncRoot)
        {
            return CopyMatchStats(matchStats);
        }
    }

    public ClientCommandResult SubmitStats(Guid matchId, int goals, int assists)
    {
        lock (syncRoot)
        {
            if (matchId != matchStats.MatchId)
            {
                return ClientCommandResult.Failure("match_not_found", "The match was not found.");
            }

            if (goals < 0 || assists < 0)
            {
                return ClientCommandResult.Failure(
                    "invalid_stats",
                    "Goals and assists cannot be negative.");
            }

            matchStats = matchStats with
            {
                Goals = goals,
                Assists = assists,
                IsPendingConfirmation = true
            };

            return ClientCommandResult.Success;
        }
    }

    public ClientCommandResult ConfirmStats(Guid matchId, Guid playerId)
    {
        lock (syncRoot)
        {
            if (matchId != matchStats.MatchId)
            {
                return ClientCommandResult.Failure("match_not_found", "The match was not found.");
            }

            var submissions = matchStats.TeammateSubmissions.ToArray();
            var index = Array.FindIndex(submissions, item => item.Player.Id == playerId);
            if (index < 0)
            {
                return ClientCommandResult.Failure("player_not_found", "The player was not found.");
            }

            submissions[index] = submissions[index] with { IsConfirmed = true };
            matchStats = matchStats with
            {
                TeammateSubmissions = Array.AsReadOnly(submissions)
            };

            return ClientCommandResult.Success;
        }
    }

    public IReadOnlyList<RateableTeammateDto> GetRateableTeammates(Guid raterId)
    {
        lock (syncRoot)
        {
            return CopyRateableTeammates(
                rateableTeammates.Where(item => item.Player.Id != raterId));
        }
    }

    public ClientCommandResult SubmitRatings(
        Guid matchId,
        Guid raterId,
        IReadOnlyList<TeammateRatingDto> ratings)
    {
        lock (syncRoot)
        {
            if (matchId != matchStats.MatchId)
            {
                return ClientCommandResult.Failure("match_not_found", "The match was not found.");
            }

            if (ratings.Any(rating =>
                    rating.PlayerId == raterId
                    || rating.Rating is < 0 or > 10)
                || ratings.Count(rating => rating.IsMvp) > 1
                || ratings.Select(rating => rating.PlayerId).Distinct().Count() != ratings.Count)
            {
                return ClientCommandResult.Failure(
                    "invalid_ratings",
                    "Ratings must target unique teammates, use scores from 0 to 10, and select at most one MVP.");
            }

            var rateablePlayerIds = rateableTeammates
                .Select(item => item.Player.Id)
                .ToHashSet();
            if (ratings.Any(rating => !rateablePlayerIds.Contains(rating.PlayerId)))
            {
                return ClientCommandResult.Failure(
                    "player_not_rateable",
                    "Ratings may only target players in the rateable teammate list.");
            }

            var submitted = ratings.ToDictionary(rating => rating.PlayerId);
            rateableTeammates = Array.AsReadOnly(
                rateableTeammates
                    .Select(item => submitted.TryGetValue(item.Player.Id, out var rating)
                        ? item with
                        {
                            Rating = rating.Rating,
                            IsLiked = rating.IsLiked,
                            IsMvp = rating.IsMvp
                        }
                        : item)
                    .ToArray());

            return ClientCommandResult.Success;
        }
    }

    // --- ADMIN-4: create and publish session ------------------------------------------------

    public CreateSessionDefaultsDto GetCreateSessionDefaults() => SeedFixtures.CreateSessionDefaults;

    public IReadOnlyList<ManagedSessionDto> ListManagedSessions()
    {
        lock (syncRoot)
        {
            var fixedSessions = SeedFixtures.Dashboard.ComingUpSessions
                .Prepend(SeedFixtures.Dashboard.FeaturedSession)
                .Select(ResolveSummary);

            return Array.AsReadOnly(
                fixedSessions
                    .Concat(publishedSummaries.Values)
                    .Select(ApplyRosterState)
                    .OrderBy(session => session.StartsAtUtc)
                    .Select(ToManagedSession)
                    .ToArray());
        }
    }

    public ManagedSessionEditDto? GetSessionForEdit(Guid sessionId)
    {
        lock (syncRoot)
        {
            return managedSessionCommands.TryGetValue(sessionId, out var command)
                ? new ManagedSessionEditDto(sessionId, command, true)
                : null;
        }
    }
    public IReadOnlyList<VenueDto> SearchVenues(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return SeedFixtures.Venues;
        }

        var trimmed = query.Trim();
        return Array.AsReadOnly(
            SeedFixtures.Venues
                .Where(venue =>
                    venue.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                    || venue.Locality.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                .ToArray());
    }

    public CreateSessionResult CreateDraft(CreateSessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateCommand(command);
        if (validation is not null)
        {
            return validation;
        }

        lock (syncRoot)
        {
            var draftId = Guid.NewGuid();
            drafts[draftId] = command;
            return CreateSessionResult.Success(draftId);
        }
    }

    public CreateSessionResult UpdateSession(Guid sessionId, CreateSessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = ValidateCommand(command);
        if (validation is not null)
        {
            return validation;
        }

        lock (syncRoot)
        {
            if (!managedSessionCommands.ContainsKey(sessionId))
            {
                return CreateSessionResult.Failure("session_not_found", "The session was not found.");
            }

            managedSessionCommands[sessionId] = command;
            var summary = BuildSummary(sessionId, command);
            var detail = BuildDetail(sessionId, command);

            if (publishedSummaries.ContainsKey(sessionId))
            {
                publishedSummaries[sessionId] = summary;
                publishedDetails[sessionId] = detail;
            }
            else
            {
                managedSummaryOverrides[sessionId] = summary;
                managedDetailOverrides[sessionId] = detail;
            }

            return CreateSessionResult.Success(sessionId);
        }
    }
    public CreateSessionResult Publish(Guid draftId)
    {
        lock (syncRoot)
        {
            // Idempotent: re-publishing the same draft returns the original session, never a duplicate.
            if (publishedSessionByDraft.TryGetValue(draftId, out var existingSessionId))
            {
                return CreateSessionResult.Success(existingSessionId);
            }

            if (!drafts.TryGetValue(draftId, out var command))
            {
                return CreateSessionResult.Failure("draft_not_found", "The session draft was not found.");
            }

            var sessionId = Guid.NewGuid();
            publishedSummaries[sessionId] = BuildSummary(sessionId, command);
            publishedDetails[sessionId] = BuildDetail(sessionId, command);
            managedSessionCommands[sessionId] = command;
            rosters[sessionId] = new RosterDto(sessionId, [], []);
            publishedSessionByDraft[draftId] = sessionId;

            return CreateSessionResult.Success(sessionId);
        }
    }

    private static CreateSessionResult? ValidateCommand(CreateSessionCommand command)
    {
        if (command.GameDateLocal.Date == DateTime.MinValue.Date)
        {
            return CreateSessionResult.Failure("date_required", "Choose a game date before publishing.");
        }

        if (!IsTimeWithinDay(command.StartTimeLocal) || command.StartTimeLocal == TimeSpan.Zero)
        {
            return CreateSessionResult.Failure("start_time_required", "Choose a start time before publishing.");
        }

        if (string.IsNullOrWhiteSpace(command.VenueName))
        {
            return CreateSessionResult.Failure("venue_required", "Choose a venue before publishing.");
        }

        if (string.IsNullOrWhiteSpace(command.Format))
        {
            return CreateSessionResult.Failure("format_required", "Select a game format before publishing.");
        }

        if (command.Capacity < 1)
        {
            return CreateSessionResult.Failure("capacity_invalid", "Capacity must be at least 1.");
        }

        if (command.TeamCount is < 2 or > 4)
        {
            return CreateSessionResult.Failure("team_count_invalid", "Choose 2, 3, or 4 teams before publishing.");
        }

        if (!IsTimeWithinDay(command.CheckInOpenLocal)
            || !IsTimeWithinDay(command.CheckInCloseLocal)
            || command.CheckInOpenLocal >= command.CheckInCloseLocal)
        {
            return CreateSessionResult.Failure(
                "checkin_window_invalid",
                "Check-in must open before it closes.");
        }

        if (command.RsvpDeadlineLocal is null)
        {
            return CreateSessionResult.Failure(
                "rsvp_deadline_required",
                "Choose an RSVP deadline before publishing.");
        }

        if (!IsTimeWithinDay(command.RsvpDeadlineLocal.Value))
        {
            return CreateSessionResult.Failure(
                "rsvp_deadline_invalid",
                "RSVP deadline must be a valid venue-local time.");
        }

        if (command.RsvpDeadlineLocal.Value > command.StartTimeLocal)
        {
            return CreateSessionResult.Failure(
                "rsvp_deadline_after_start",
                "RSVP deadline cannot be after session start.");
        }

        return null;
    }

    private SessionSummaryDto ResolveSummary(SessionSummaryDto session) =>
        managedSummaryOverrides.TryGetValue(session.Id, out var updated)
            ? updated
            : session;

    private static ManagedSessionDto ToManagedSession(SessionSummaryDto session) =>
        new(
            session.Id,
            session.Title,
            session.DateLabel,
            session.TimeLabel,
            session.Venue,
            session.Format,
            session.Capacity,
            session.StatusLabel);

    private static CreateSessionCommand ToEditableCommand(SessionDetailDto session)
    {
        var localStart = DateTime.SpecifyKind(session.StartsAtUtc, DateTimeKind.Unspecified);
        var venue = SeedFixtures.Venues.FirstOrDefault(
            item => item.Name.Equals(session.Venue, StringComparison.OrdinalIgnoreCase));

        return new CreateSessionCommand(
            localStart.Date,
            localStart.TimeOfDay,
            localStart.TimeOfDay - TimeSpan.FromMinutes(10),
            localStart.TimeOfDay + TimeSpan.FromMinutes(5),
            venue?.Id,
            session.Venue,
            session.Format,
            session.Capacity,
            2,
            localStart.TimeOfDay - TimeSpan.FromHours(1));
    }
    private static SessionSummaryDto BuildSummary(Guid sessionId, CreateSessionCommand command) =>
        new(
            sessionId,
            $"{command.VenueName} · {command.Format}",
            command.VenueName,
            command.Format,
            ToStartUtc(command),
            FormatDateLabel(command.GameDateLocal),
            FormatTimeLabel(command.StartTimeLocal),
            "Open",
            0,
            command.Capacity,
            false,
            0,
            $"RSVP closes {FormatTimeLabel(command.RsvpDeadlineLocal!.Value)}");

    private static SessionDetailDto BuildDetail(Guid sessionId, CreateSessionCommand command) =>
        new(
            sessionId,
            "Saturday pickup",
            command.VenueName,
            $"{command.VenueName} · {command.Format}",
            command.Format,
            ToStartUtc(command),
            $"{FormatDateLabel(command.GameDateLocal)} · {FormatTimeLabel(command.StartTimeLocal)}",
            0,
            command.Capacity,
            $"closes {FormatTimeLabel(command.RsvpDeadlineLocal!.Value)}",
            false,
            true,
            false);

    private static bool IsTimeWithinDay(TimeSpan time) =>
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    private bool TryGetCapacity(Guid sessionId, out int capacity)
    {
        if (managedDetailOverrides.TryGetValue(sessionId, out var updatedSession))
        {
            capacity = updatedSession.Capacity;
            return true;
        }

        if (SeedFixtures.Sessions.TryGetValue(sessionId, out var session))
        {
            capacity = session.Capacity;
            return true;
        }

        if (publishedDetails.TryGetValue(sessionId, out var published))
        {
            capacity = published.Capacity;
            return true;
        }

        capacity = 0;
        return false;
    }

    // Seed only: labels off the venue-local fields, so this just tags the local clock as UTC for the
    // StartsAtUtc slot. The real backend must perform a genuine venue-local -> UTC conversion here.
    private static DateTime ToStartUtc(CreateSessionCommand command) =>
        DateTime.SpecifyKind(command.GameDateLocal.Date.Add(command.StartTimeLocal), DateTimeKind.Utc);

    private static string FormatDateLabel(DateTime date) =>
        date.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string FormatTimeLabel(TimeSpan time) =>
        (DateTime.MinValue + time).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private SessionSummaryDto ApplyRosterState(SessionSummaryDto session)
    {
        var roster = rosters[session.Id];
        var goingCount = roster.Going.Count;
        var isFull = goingCount >= session.Capacity;
        var currentPlayerGoing = roster.Going.Any(
            entry => entry.Player.Id == SeedFixtures.CurrentPlayerId);

        return session with
        {
            GoingCount = goingCount,
            IsFull = isFull,
            WaitlistCount = roster.Waitlist.Count,
            StatusLabel = currentPlayerGoing ? "You're going" : isFull ? "Full" : "Open"
        };
    }

    private static IReadOnlyList<WaitlistEntryDto> ReorderWaitlist(
        IReadOnlyList<WaitlistEntryDto> waitlist) =>
        Array.AsReadOnly(
            waitlist.Select(
                    (entry, index) => entry with { Position = index + 1 })
                .ToArray());

    private static RosterDto CopyRoster(RosterDto roster) =>
        new(
            roster.SessionId,
            Array.AsReadOnly(roster.Going.ToArray()),
            Array.AsReadOnly(roster.Waitlist.ToArray()));

    private static MatchStatsDto CopyMatchStats(MatchStatsDto stats) =>
        stats with
        {
            TeammateSubmissions = Array.AsReadOnly(stats.TeammateSubmissions.ToArray())
        };

    private static IReadOnlyList<RateableTeammateDto> CopyRateableTeammates(
        IEnumerable<RateableTeammateDto> teammates) =>
        Array.AsReadOnly(teammates.ToArray());
}



