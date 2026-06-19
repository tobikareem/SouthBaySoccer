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
        }
    }

    public SessionsDashboardDto GetDashboard()
    {
        lock (syncRoot)
        {
            var featured = ApplyRosterState(SeedFixtures.Dashboard.FeaturedSession);
            var comingUp = SeedFixtures.Dashboard.ComingUpSessions
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
            if (!SeedFixtures.Sessions.TryGetValue(sessionId, out var session))
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
                || !SeedFixtures.Sessions.TryGetValue(sessionId, out var session))
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

                if (going.Count >= session.Capacity)
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
                || !SeedFixtures.Sessions.TryGetValue(sessionId, out var session))
            {
                return ClientCommandResult.Failure("session_not_found", "The session was not found.");
            }

            if (roster.Going.Count < session.Capacity)
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
