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
        // Attend reseeds the shared roster on each call; attend the asserted (newest) session last
        // so its checked-in ids match the roster the handler reads.
        context.Attend(older, going: true);
        context.Attend(newest, going: true);

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
    public async Task HandleAsync_BuildsTeamSheetsWithCaptainsAndApprovedGoals()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Drafted game");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        var captain = context.GoingRoster[0];
        var scorer = context.GoingRoster[1];
        var quiet = context.GoingRoster[2];
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.InProgress };
        var team = new MatchTeam
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            TeamNumber = 1,
            Name = "Team Vic",
            CaptainPlayerProfileId = captain.PlayerProfileId,
        };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([team]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { captain, scorer, quiet }
                .Select(member => new TeamAssignment
                {
                    MatchId = match.Id,
                    MatchTeamId = team.Id,
                    PlayerProfileId = member.PlayerProfileId,
                })
                .ToArray());
        context.Stats
            .Setup(x => x.ListMatchEventsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                GoalEvent(match.Id, team.Id, scorer.PlayerProfileId, MatchEventReviewStatus.Approved),
                GoalEvent(match.Id, team.Id, scorer.PlayerProfileId, MatchEventReviewStatus.Approved),
                AssistEvent(match.Id, team.Id, scorer.PlayerProfileId, MatchEventReviewStatus.Approved),
                // Pending goals are not fact yet and stay off the summary.
                GoalEvent(match.Id, team.Id, quiet.PlayerProfileId, MatchEventReviewStatus.Pending),
            ]);

        var summary = await context.CreateHandler().HandleAsync();

        var sheet = summary!.Teams.Should().ContainSingle().Subject;
        sheet.Name.Should().Be("Team Vic");
        sheet.CaptainName.Should().Be(captain.DisplayName);
        sheet.Members.Should().HaveCount(3);
        sheet.Members[0].IsCaptain.Should().BeTrue("captain leads the sheet");
        sheet.Members[1].DisplayName.Should().Be(scorer.DisplayName);
        sheet.Members[1].Goals.Should().Be(2, "only approved goals count");
        sheet.Members[1].Assists.Should().Be(1, "only approved assists count");
        sheet.Members.Single(member => member.PlayerProfileId == quiet.PlayerProfileId).Goals.Should().Be(0);
        sheet.ResultLabel.Should().BeEmpty("the match is not settled yet");
    }

    [Fact]
    public async Task HandleAsync_CountsComeFromTheDisplayRosterIncludingWaitlist()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Busy game");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        context.WaitlistRoster.AddRange(Enumerable.Range(0, 5)
            .Select(index => new RosterMemberRecord(Guid.NewGuid(), $"Waiter {index}", string.Empty, false, index + 1)));

        var summary = await context.CreateHandler().HandleAsync();

        summary!.GoingCount.Should().Be(14);
        summary.WaitlistCount.Should().Be(5);
        summary.CheckedInCount.Should().Be(12);
    }

    [Fact]
    public async Task HandleAsync_WhenAdminAndTeamsNeverLocked_OffersLockAndMatchActions()
    {
        var context = new TestContext();
        // Yesterday's game sits inside the 3-day admin edit window.
        var session = context.SessionAt(Utc(2026, 7, 22, 3, 0), "Unfinished game");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.ImportedParticipants.Add(new PickupPalGameParticipant
        {
            SessionId = session.Id,
            PickupPalParticipantId = "pp-tob8",
            DisplayName = "tob8",
            IsWaitlist = true,
        });

        var summary = await context.CreateHandler().HandleAsync();

        summary!.CanLockTeams.Should().BeTrue("no match exists yet, so teams were never locked");
        summary.CanMatchPlayers.Should().BeTrue("an unlinked imported name is still on the roster");
        summary.CanApprovePostGame.Should().BeFalse("there is no locked match to approve");
    }

    [Fact]
    public async Task HandleAsync_WhenCaptainOfLockedMatch_OffersResultAndGoalConfirmation()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Awaiting confirmation");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.InProgress };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchTeam
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                TeamNumber = 1,
                Name = "Team Ada",
                CaptainPlayerProfileId = context.Profile.Id,
            }]);

        var summary = await context.CreateHandler().HandleAsync();

        summary!.CanApprovePostGame.Should().BeTrue("the viewer captains a team on an unsettled match");
        summary.CanLockTeams.Should().BeFalse("locking is a game-admin action");
        summary.CanMatchPlayers.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenAdminOnDraftMatchWithLockableTeams_OffersResultConfirmation()
    {
        // The post-game screen can finalize a played-but-never-locked game for an admin (the first
        // recorded result auto-locks), so the Game Day row mirrors that exactly.
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Never locked");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        var captainA = context.GoingRoster[0];
        var captainB = context.GoingRoster[1];
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.Draft };
        var teams = new[] { captainA, captainB }
            .Select((captain, index) => new MatchTeam
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                TeamNumber = index + 1,
                Name = $"Team {index + 1}",
                CaptainPlayerProfileId = captain.PlayerProfileId,
            })
            .ToArray();
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams
                .Select(team => new TeamAssignment
                {
                    MatchId = match.Id,
                    MatchTeamId = team.Id,
                    PlayerProfileId = team.CaptainPlayerProfileId!.Value,
                })
                .ToArray());

        var summary = await context.CreateHandler().HandleAsync();

        summary!.CanApprovePostGame.Should().BeTrue("an admin can confirm a lockable draft game");
        summary.CanLockTeams.Should().BeTrue("two days ago is still inside the 3-day admin edit window");
    }

    [Fact]
    public async Task HandleAsync_WhenDraftTeamCaptainIsOrphaned_WithholdsResultConfirmation()
    {
        // A merge that failed to re-point the captaincy leaves a team whose captain has no
        // assignment; the auto-lock path would reject it, so the row must not be offered.
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Orphaned captain");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.Draft };
        var team = new MatchTeam
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            TeamNumber = 1,
            Name = "Team 1",
            CaptainPlayerProfileId = Guid.NewGuid(),
        };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([team]);

        var summary = await context.CreateHandler().HandleAsync();

        summary!.CanApprovePostGame.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenRegularPlayerAttended_OffersTeammateRatings()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 21, 3, 0), "Player follow-up");
        context.ConfigureSessions(session);
        context.Attend(session, going: true);
        context.GoingRoster[0] = context.GoingRoster[0] with { PlayerProfileId = context.Profile.Id };
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, Status = MatchStatus.InProgress };
        context.Stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        context.Stats
            .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var summary = await context.CreateHandler().HandleAsync();

        summary!.CanLockTeams.Should().BeFalse();
        summary.CanMatchPlayers.Should().BeFalse();
        summary.CanApprovePostGame.Should().BeFalse();
        summary.MatchId.Should().Be(match.Id);
        summary.CanRateTeammates.Should().BeTrue();
    }

    private static MatchEvent GoalEvent(
        Guid matchId,
        Guid teamId,
        Guid playerProfileId,
        MatchEventReviewStatus reviewStatus) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchTeamId = teamId,
            PlayerProfileId = playerProfileId,
            EventType = MatchEventType.Goal,
            ReviewStatus = reviewStatus,
        };

    private static MatchEvent AssistEvent(
        Guid matchId,
        Guid teamId,
        Guid assistPlayerProfileId,
        MatchEventReviewStatus reviewStatus) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchTeamId = teamId,
            AssistPlayerProfileId = assistPlayerProfileId,
            EventType = MatchEventType.Goal,
            ReviewStatus = reviewStatus,
        };

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
            // The display roster behind the counts and team-sheet names.
            Rsvps
                .Setup(x => x.ListGoingRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, CancellationToken _) => GoingRoster);
            Rsvps
                .Setup(x => x.ListActiveWaitlistRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, CancellationToken _) => WaitlistRoster);
            PickupPalGames
                .Setup(x => x.ListParticipantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, CancellationToken _) => ImportedParticipants);
            Profiles
                .Setup(x => x.ListProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PlayerProfile>());
            Stats
                .Setup(x => x.ListAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TeamAssignment>());
            Stats
                .Setup(x => x.ListMatchEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<MatchEvent>());
            Stats
                .Setup(x => x.ListMatchResultsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<MatchResult>());
        }

        public List<RosterMemberRecord> GoingRoster { get; } = [];
        public List<RosterMemberRecord> WaitlistRoster { get; } = [];
        public List<PickupPalGameParticipant> ImportedParticipants { get; } = [];

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
        public Mock<IPickupPalGameRepository> PickupPalGames { get; } = new();
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

        // Counts come from the display roster (like the Game Day tiles), so attending seeds both the
        // attendance record and a 14-strong going roster with the first 12 checked in.
        public void Attend(Session session, bool going)
        {
            GoingRoster.Clear();
            GoingRoster.AddRange(Enumerable.Range(0, 14)
                .Select(index => new RosterMemberRecord(Guid.NewGuid(), $"Player {index:D2}", string.Empty, false, null)));
            var checkedInIds = GoingRoster.Take(12).Select(member => member.PlayerProfileId).ToArray();
            AttendanceBySession[session.Id] = new GameDayAttendanceRecord(14, 12, 0, going, !going, false, checkedInIds);
        }

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
            PickupPalGames.Object,
            PlayerGroups.Object,
            Stats.Object);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
