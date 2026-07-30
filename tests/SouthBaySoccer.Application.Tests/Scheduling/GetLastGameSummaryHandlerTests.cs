using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Match = SouthBaySoccer.Domain.Entities.Stats.Match;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class GetLastGameSummaryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenPlayerAttendedAPastGame_ReturnsTheMostRecentOne()
    {
        var context = new TestContext();
        var older = context.SessionAt(Utc(2026, 7, 15, 3, 0), "Older game");
        var newest = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Latest game");
        context.ConfigureSessions(newest, older);
        context.Attend(newest, going: true);
        context.Attend(older, going: true);

        var summary = await context.CreateHandler().HandleAsync();

        summary.Should().NotBeNull();
        summary!.SessionId.Should().Be(newest.Id);
        summary.Title.Should().Be("Latest game");
        summary.GoingCount.Should().Be(14);
        summary.CheckedInCount.Should().Be(12);
        summary.TeamCount.Should().Be(0);
        summary.ResultSummary.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenPlayerAttendedNone_FallsBackToTheirGroupsLatestGame()
    {
        var context = new TestContext();
        var otherGroups = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Other group's game");
        var myGroups = context.SessionAt(Utc(2026, 7, 18, 3, 0), "Bay Area game");
        context.ConfigureSessions(otherGroups, myGroups);
        context.GroupNamesBySession[otherGroups.Id] = "Torrance Tuesday";
        context.GroupNamesBySession[myGroups.Id] = "Bay Area Soccer";
        context.PlayerGroupNames.Add("bay area soccer");

        var summary = await context.CreateHandler().HandleAsync();

        summary!.SessionId.Should().Be(myGroups.Id, "another group's newer game must never surface");
        summary.GroupName.Should().Be("Bay Area Soccer");
    }

    [Fact]
    public async Task HandleAsync_WhenNothingRelevantInWindow_ReturnsNull()
    {
        var context = new TestContext();
        var unrelated = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Someone else's game");
        context.ConfigureSessions(unrelated);
        context.GroupNamesBySession[unrelated.Id] = "Torrance Tuesday";

        var summary = await context.CreateHandler().HandleAsync();

        summary.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenMatchIsPublished_IncludesTeamsAndResultSummary()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Published game");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.Published };
        var teamVic = new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, TeamNumber = 1, Name = "Team Vic" };
        var teamAde = new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, TeamNumber = 2, Name = "Team Ade" };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([teamVic, teamAde]);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MatchResult { MatchId = match.Id, MatchTeamId = teamAde.Id, Wins = 1, Draws = 1 },
                new MatchResult { MatchId = match.Id, MatchTeamId = teamVic.Id, Wins = 2 },
            ]);

        var summary = await context.CreateHandler().HandleAsync();

        summary!.TeamCount.Should().Be(2);
        summary.ResultSummary.Should().Be("Team Ade 1W 1D · Team Vic 2W");
    }

    [Fact]
    public async Task HandleAsync_WhenMatchIsStillDraft_LeavesResultSummaryNull()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Unsettled game");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.Draft };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchTeam { Id = Guid.NewGuid(), MatchId = match.Id, TeamNumber = 1, Name = "Team Vic" }]);

        var summary = await context.CreateHandler().HandleAsync();

        summary!.TeamCount.Should().Be(1);
        summary.ResultSummary.Should().BeNull("a draft match has no settled result");
    }

    [Fact]
    public async Task HandleAsync_QueriesOnlyThePastThirtyDayWindow()
    {
        var context = new TestContext();
        context.ConfigureSessions();

        _ = await context.CreateHandler().HandleAsync();

        // Clock is 2026-07-23 02:35 UTC; the venue-local (PT) day starts 2026-07-22 07:00 UTC.
        var todayStartUtc = Utc(2026, 7, 22, 7, 0);
        context.Sessions.Verify(x => x.ListPastGameDayCandidatesAsync(
            todayStartUtc.AddDays(-30),
            todayStartUtc,
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()));
    }

    private sealed class TestContext
    {
        public TestContext()
        {
            CurrentUser.SetupGet(x => x.UserId).Returns(Profile.IdentityUserId);
            Clock.SetupGet(x => x.UtcNow).Returns(Utc(2026, 7, 23, 2, 35));
            Profiles
                .Setup(x => x.FindByIdentityUserIdAsync(Profile.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Venues
                .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new Venue { Id = id, Name = "Marina Field" });
            Sessions
                .Setup(x => x.GetGroupNamesBySessionAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<Guid, string>)GroupNamesBySession
                        .Where(pair => ids.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value));
            PlayerGroups
                .Setup(x => x.ListPlayerGroupsAsync(Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => PlayerGroupNames
                    .Select((name, index) => new PlayerGroupReadModel(
                        Guid.NewGuid(), $"group-{index}@g.us", name, 20, index == 0))
                    .ToArray());
            Rsvps
                .Setup(x => x.GetGameDayAttendanceBatchAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, Guid _, CancellationToken _) =>
                    (IReadOnlyDictionary<Guid, GameDayAttendanceRecord>)ids.Distinct().ToDictionary(
                        id => id,
                        id => AttendanceBySession.GetValueOrDefault(
                            id,
                            new GameDayAttendanceRecord(0, 0, 0, false, false, false, []))));
        }

        public PlayerProfile Profile { get; } = new()
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Ada",
        };

        public Dictionary<Guid, string> GroupNamesBySession { get; } = [];
        public List<string> PlayerGroupNames { get; } = [];
        public Dictionary<Guid, GameDayAttendanceRecord> AttendanceBySession { get; } = [];

        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();
        public Mock<IVenueRepository> Venues { get; } = new();
        public Mock<IRsvpRepository> Rsvps { get; } = new();
        public Mock<IPlayerGroupLinkRepository> PlayerGroups { get; } = new();
        public Mock<IStatsRepository> Stats { get; } = new();

        public void ConfigureSessions(params Session[] newestFirst) =>
            Sessions
                .Setup(x => x.ListPastGameDayCandidatesAsync(
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(newestFirst);

        public void Attend(Session session, bool going) =>
            AttendanceBySession[session.Id] = new GameDayAttendanceRecord(14, 12, 0, going, !going, false, []);

        public Session SessionAt(DateTime startsAtUtc, string title) => new()
        {
            Id = Guid.NewGuid(),
            SeasonId = Guid.NewGuid(),
            VenueId = Guid.NewGuid(),
            Title = title,
            Format = "7v7",
            Capacity = 20,
            TeamCount = 2,
            StartsAtUtc = startsAtUtc,
            CheckInOpensAtUtc = startsAtUtc.AddMinutes(-10),
            CheckInClosesAtUtc = startsAtUtc.AddMinutes(5),
            RsvpDeadlineUtc = startsAtUtc.AddHours(-1),
            Status = SessionStatus.Published,
        };

        public GetLastGameSummaryQueryHandler CreateHandler() => new(
            CurrentUser.Object,
            Clock.Object,
            Profiles.Object,
            Sessions.Object,
            Venues.Object,
            Rsvps.Object,
            PlayerGroups.Object,
            Stats.Object);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
