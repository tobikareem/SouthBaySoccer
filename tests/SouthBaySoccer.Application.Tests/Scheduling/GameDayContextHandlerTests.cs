using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;
using Match = SouthBaySoccer.Domain.Entities.Stats.Match;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class GameDayContextHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenGoingEligibleAndInsideWindow_ReturnsOpenServerProjection()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(20, 7, 1, true, false, false, []));

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(session.Id);
        result.Status.Should().Be("Open");
        result.IsSelfCheckInAvailable.Should().BeTrue();
        result.GoingCount.Should().Be(20);
        result.CheckedInCount.Should().Be(7);
        result.LateCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WhenWaitlisted_ReturnsBlockedWithoutCheckingEligibility()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(20, 7, 1, false, true, false, []));

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.Status.Should().Be("Blocked");
        result.IsSelfCheckInAvailable.Should().BeFalse();
        result.BlockReason.Should().Contain("waitlisted");
        context.Eligibility.Verify(
            x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherSessionIsInProgress_PrefersPlayersConfirmedSession()
    {
        var context = new TestContext();
        var inProgressForOthers = context.SessionAt(Utc(2026, 7, 23, 2, 35));
        var confirmedForPlayer = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([inProgressForOthers, confirmedForPlayer]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(
                inProgressForOthers.Id,
                context.Profile.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(20, 10, 0, false, false, false, []));
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(
                confirmedForPlayer.Id,
                context.Profile.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(18, 4, 0, true, false, false, []));

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(confirmedForPlayer.Id);
        result.IsCurrentPlayerGoing.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenPlayerIsGoingToTwoGames_ListsBothAndCanSelectEither()
    {
        var context = new TestContext();
        var earlier = context.SessionAt(Utc(2026, 7, 23, 2, 0));
        var later = context.SessionAt(Utc(2026, 7, 23, 3, 0));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlier, later]);
        foreach (var game in new[] { earlier, later })
        {
            context.Rsvps
                .Setup(x => x.GetGameDayAttendanceAsync(game.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GameDayAttendanceRecord(12, 0, 0, true, false, false, []));
        }

        // Default: both games are offered, exactly one is marked selected.
        var auto = await context.CreateHandler().HandleAsync();
        auto!.TodaysGames.Select(game => game.SessionId).Should().BeEquivalentTo([earlier.Id, later.Id]);
        auto.TodaysGames.Should().ContainSingle(game => game.IsSelected)
            .Which.SessionId.Should().Be(auto.SessionId);

        // Explicit pick loads that game and marks it selected in the list.
        var picked = await context.CreateHandler().HandleAsync(earlier.Id);
        picked!.SessionId.Should().Be(earlier.Id);
        picked.TodaysGames.Single(game => game.SessionId == earlier.Id).IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAdminIsInsideDraftWindow_EnablesCaptainAssignment()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.ConfigureSession(session);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanAssignCaptains.Should().BeTrue();
        result.CanDraftTeam.Should().BeFalse();
        result.CanApprovePostGame.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenAdminIsBeforeCheckInOpens_StillEnablesCaptainAssignment()
    {
        var context = new TestContext();
        // Check-in opens at 04:50 and the clock reads 02:35, so this is well before the window.
        var session = context.SessionAt(Utc(2026, 7, 23, 5, 0));
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.ConfigureSession(session);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanAssignCaptains.Should().BeTrue("game admins set teams up ahead of game day");
    }

    [Fact]
    public async Task HandleAsync_WhenAdminIsBeforeCheckInOpens_StillEnablesTeamDraft()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 5, 0));
        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            MatchNumber = 1,
            Status = MatchStatus.Draft,
        };
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.ConfigureSession(session);
        context.ConfigureMatch(session, match);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanDraftTeam.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenCaptainIsBeforeCheckInOpens_WithholdsTeamDraft()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 5, 0));
        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            MatchNumber = 1,
            Status = MatchStatus.Draft,
        };
        context.ConfigureSession(session);
        // ConfigureMatch seats the current profile as the team captain, but they are not an admin.
        context.ConfigureMatch(session, match);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanDraftTeam.Should().BeFalse("captains still wait for game-day check-in");
        result.CanAssignCaptains.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenCallerCaptainsDraftTeam_EnablesOnlyTeamDraft()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            MatchNumber = 1,
            Status = MatchStatus.Draft,
        };
        context.ConfigureSession(session);
        context.ConfigureMatch(session, match);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanAssignCaptains.Should().BeFalse();
        result.CanDraftTeam.Should().BeTrue();
        result.CanApprovePostGame.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenAdminAndMatchInDraft_EnablesTeamDraftForAdmin()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            MatchNumber = 1,
            Status = MatchStatus.Draft,
        };
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.ConfigureSession(session);
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
                Name = "Team Green",
                CaptainPlayerProfileId = Guid.NewGuid(),
            }]);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanAssignCaptains.Should().BeTrue();
        result.CanDraftTeam.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAdmin_ProjectsGoingWaitlistRosterWithCheckInFlags()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var goingId = Guid.NewGuid();
        var waitlistId = Guid.NewGuid();
        context.CurrentUser.Setup(x => x.HasPolicy("CanCheckInPlayers")).Returns(true);
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(2, 1, 0, true, false, false, [goingId]));
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RosterMemberRecord(goingId, "Amina", "Midfielder", false, null)]);
        context.Rsvps
            .Setup(x => x.ListActiveWaitlistRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RosterMemberRecord(waitlistId, "Bola", "Forward", false, 1)]);
        context.PickupPalGames
            .Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanManageCheckIns.Should().BeTrue();
        result.Roster.Should().HaveCount(2);
        var going = result.Roster.Single(entry => entry.PlayerProfileId == goingId);
        going.IsWaitlist.Should().BeFalse();
        going.IsCheckedIn.Should().BeTrue();
        var waitlisted = result.Roster.Single(entry => entry.PlayerProfileId == waitlistId);
        waitlisted.IsWaitlist.Should().BeTrue();
        waitlisted.IsCheckedIn.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenLockedMatchReachesPostGame_EnablesCaptainApproval()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var match = new Match
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            MatchNumber = 1,
            Status = MatchStatus.InProgress,
        };
        context.Clock.SetupGet(x => x.UtcNow).Returns(session.StartsAtUtc.AddMinutes(100));
        context.ConfigureSession(session);
        context.ConfigureMatch(session, match);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.CanAssignCaptains.Should().BeFalse();
        result.CanDraftTeam.Should().BeFalse();
        result.CanApprovePostGame.Should().BeTrue();
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
            Eligibility
                .Setup(x => x.CheckAsync(Profile.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlayerSessionEligibilityResult(true, null));
            Rsvps
                .Setup(x => x.ListGoingRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<RosterMemberRecord>());
            Rsvps
                .Setup(x => x.ListActiveWaitlistRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<RosterMemberRecord>());
            PickupPalGames
                .Setup(x => x.ListParticipantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PickupPalGameParticipant>());
            // The real repository returns an empty list, never null; without this default an
            // unstubbed read would hand the handler a null collection.
            Stats
                .Setup(x => x.ListAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TeamAssignment>());
        }

        public PlayerProfile Profile { get; } = new()
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Ada"
        };

        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();
        public Mock<IVenueRepository> Venues { get; } = new();
        public Mock<IRsvpRepository> Rsvps { get; } = new();
        public Mock<IPickupPalGameRepository> PickupPalGames { get; } = new();
        public Mock<IStatsRepository> Stats { get; } = new();
        public Mock<IPlayerSessionEligibilityService> Eligibility { get; } = new();

        public void ConfigureSession(Session session)
        {
            Sessions
                .Setup(x => x.ListGameDayCandidatesAsync(
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([session]);
            Rsvps
                .Setup(x => x.GetGameDayAttendanceAsync(session.Id, Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GameDayAttendanceRecord(20, 7, 0, true, false, false, []));
        }

        public void ConfigureMatch(Session session, Match match)
        {
            Stats
                .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(match);
            Stats
                .Setup(x => x.ListMatchTeamsAsync(match.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new MatchTeam
                {
                    Id = Guid.NewGuid(),
                    MatchId = match.Id,
                    TeamNumber = 1,
                    Name = "Team Green",
                    CaptainPlayerProfileId = Profile.Id,
                }]);
        }

        public Session SessionAt(DateTime startsAtUtc) => new()
        {
            Id = Guid.NewGuid(),
            SeasonId = Guid.NewGuid(),
            VenueId = Guid.NewGuid(),
            Title = "Wednesday Pickup",
            Format = "7v7",
            Capacity = 20,
            TeamCount = 2,
            StartsAtUtc = startsAtUtc,
            CheckInOpensAtUtc = startsAtUtc.AddMinutes(-10),
            CheckInClosesAtUtc = startsAtUtc.AddMinutes(5),
            RsvpDeadlineUtc = startsAtUtc.AddHours(-1),
            Status = SessionStatus.Published
        };

        public GetTodayGameDayContextQueryHandler CreateHandler() => new(
            CurrentUser.Object,
            Clock.Object,
            Profiles.Object,
            Sessions.Object,
            Venues.Object,
            Rsvps.Object,
            PickupPalGames.Object,
            Stats.Object,
            Eligibility.Object);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
