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
    public async Task HandleAsync_WhenWaitlistedEligibleAndInsideWindow_AllowsCheckIn()
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
        result!.Status.Should().Be("Open");
        result.IsSelfCheckInAvailable.Should().BeTrue("waitlisted players may now check in at the field");
        context.Eligibility.Verify(
            x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
    public async Task HandleAsync_WhenPlayerGoingToOneOfSeveralGames_HidesTheOthers()
    {
        var context = new TestContext();
        var mine = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var otherGroups = context.SessionAt(Utc(2026, 7, 23, 3, 0));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([mine, otherGroups]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(mine.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(12, 0, 0, true, false, false, []));

        var result = await context.CreateHandler().HandleAsync();

        result!.SessionId.Should().Be(mine.Id);
        result.TodaysGames.Should().ContainSingle("games the player has no spot on and no group tie to are hidden")
            .Which.SessionId.Should().Be(mine.Id);
        result.IsSpectator.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenPlayerOnlyWaitlisted_TreatsGameAsTheirs()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.Rsvps
            .Setup(x => x.GetGameDayAttendanceAsync(session.Id, context.Profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameDayAttendanceRecord(20, 0, 0, false, true, false, []));

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull("a waitlisted player holds a spot on the day");
        result!.IsSpectator.Should().BeFalse();
        context.PlayerGroups.Verify(
            x => x.ListPlayerGroupsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an attending pool never needs the group lookup");
    }

    [Fact]
    public async Task HandleAsync_WhenNoRsvpButGroupGameToday_ReturnsReadOnlySpectatorContext()
    {
        var context = new TestContext();
        // Kick-off 05:00 keeps the RSVP deadline (04:00) ahead of the 02:35 test clock, so Join is open.
        var session = context.SessionAt(Utc(2026, 7, 23, 5, 0));
        context.CurrentUser.Setup(x => x.HasPolicy("CanCheckInPlayers")).Returns(true);
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        // Trim + case differences between the snapshot name and the player's group must still match.
        context.GroupNamesBySession[session.Id] = " Bay Area Soccer ";
        context.PlayerGroupNames.Add("bay area soccer");

        var result = await context.CreateHandler().HandleAsync();

        result.Should().NotBeNull();
        result!.IsSpectator.Should().BeTrue();
        result.GroupName.Should().Be(" Bay Area Soccer ");
        result.StatusLabel.Should().Be("Spectator");
        result.IsSelfCheckInAvailable.Should().BeFalse();
        result.CanJoin.Should().BeTrue("RSVP is still open");
        result.JoinBlockedReason.Should().BeNull();
        result.Capacity.Should().Be(session.Capacity);
        // Read-only: every profile-keyed action is withheld, even the check-in policy the caller holds.
        result.CanManageCheckIns.Should().BeFalse();
        result.CanLateCheckIn.Should().BeFalse();
        result.CanAssignCaptains.Should().BeFalse();
        result.CanDraftTeam.Should().BeFalse();
        result.CanApprovePostGame.Should().BeFalse();
        result.CanSubmitOwnStats.Should().BeFalse();
        result.CanViewTeams.Should().BeFalse();
        result.LateCheckInPlayers.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenSpectatorAndRsvpDeadlinePassed_BlocksJoinWithReason()
    {
        var context = new TestContext();
        // Deadline is StartsAt - 1h = 01:40; the 02:35 test clock is past it.
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.GroupNamesBySession[session.Id] = "Bay Area Soccer";
        context.PlayerGroupNames.Add("Bay Area Soccer");

        var result = await context.CreateHandler().HandleAsync();

        result!.IsSpectator.Should().BeTrue();
        result.CanJoin.Should().BeFalse();
        result.JoinBlockedReason.Should().Be("RSVP is closed for this game.");
    }

    [Fact]
    public async Task HandleAsync_WhenNoRsvpAndNoGroupMatch_ReturnsNull()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.GroupNamesBySession[session.Id] = "Torrance Tuesday";
        context.PlayerGroupNames.Add("Bay Area Soccer");

        var result = await context.CreateHandler().HandleAsync();

        result.Should().BeNull("another group's game is completely hidden");
    }

    [Fact]
    public async Task HandleAsync_WhenManualSessionHasNoGroup_StaysHiddenFromNonAttendees()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        context.PlayerGroupNames.Add("Bay Area Soccer");

        var result = await context.CreateHandler().HandleAsync();

        result.Should().BeNull("a hand-created session carries no group and was not RSVP'd to");
    }

    [Fact]
    public async Task HandleAsync_WhenNonAdminRequestsAllGames_IgnoresTheFlag()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);

        var result = await context.CreateHandler().HandleAsync(null, showAllGames: true);

        result.Should().BeNull("only game admins may widen to every game");
    }

    [Fact]
    public async Task HandleAsync_WhenAdminRequestsAllGames_ReturnsEveryGameWithFullControls()
    {
        var context = new TestContext();
        var mine = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var other = context.SessionAt(Utc(2026, 7, 23, 3, 0));
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.CurrentUser.Setup(x => x.HasPolicy("CanCheckInPlayers")).Returns(true);
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([mine, other]);

        var result = await context.CreateHandler().HandleAsync(other.Id, showAllGames: true);

        result!.SessionId.Should().Be(other.Id);
        result.TodaysGames.Should().HaveCount(2);
        result.IsShowingAllGames.Should().BeTrue();
        result.CanShowAllGames.Should().BeTrue();
        result.IsSpectator.Should().BeFalse("an admin who widened the pool is running the day, not spectating");
        result.CanManageCheckIns.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAdminHasNoRelevantGameAndNoToggle_ReturnsNull()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        context.Sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);

        var result = await context.CreateHandler().HandleAsync();

        result.Should().BeNull("the old show-everything fallback is gone; admins use the toggle");
        result = await context.CreateHandler().HandleAsync(null, showAllGames: true);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_WhenParticipant_CarriesTitleGroupAndCapacityForTheHeader()
    {
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        context.ConfigureSession(session);
        context.GroupNamesBySession[session.Id] = "Bay Area Soccer";

        var result = await context.CreateHandler().HandleAsync();

        result!.Title.Should().Be("Wednesday Pickup");
        result.GroupName.Should().Be("Bay Area Soccer");
        result.Capacity.Should().Be(20);
        result.CanJoin.Should().BeFalse("the player already holds a spot");
    }

    [Fact]
    public async Task HandleAsync_WhenRosteredPlayerDuringDraft_CanWatchTheTeamsView()
    {
        // Players get the read-only teams view even mid-draft: the view labels the live state
        // (whose turn, who's unpicked) instead of hiding until lock.
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var match = new Match { Id = Guid.NewGuid(), SessionId = session.Id, MatchNumber = 1, Status = MatchStatus.Draft };
        context.ConfigureSession(session);
        context.ConfigureMatch(session, match);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RosterMemberRecord(context.Profile.Id, "Ada", "MID", false, null)]);

        var result = await context.CreateHandler().HandleAsync();

        result!.CanViewTeams.Should().BeTrue("a rostered player may watch the draft read-only");
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
    public async Task HandleAsync_WhenImportedParticipantsAreUnlinked_KeepsThemOnTheDisplayRoster()
    {
        // The bug this covers: unlinked imports were dropped from the roster, so a 26-strong
        // waitlist rendered as 11 and the tile disagreed with the list behind it.
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var waitlistId = Guid.NewGuid();
        context.Clock.SetupGet(x => x.UtcNow).Returns(session.CheckInOpensAtUtc.AddMinutes(1));
        context.ConfigureSession(session);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        context.Rsvps
            .Setup(x => x.ListActiveWaitlistRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RosterMemberRecord(waitlistId, "Bola", "Forward", false, 1)]);
        context.PickupPalGames
            .Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PickupPalGameParticipant
                {
                    SessionId = session.Id,
                    PlayerProfileId = null,
                    PickupPalParticipantId = "pp-victor",
                    DisplayName = "victor",
                    IsWaitlist = true,
                    DisplayOrder = 2,
                },
                new PickupPalGameParticipant
                {
                    SessionId = session.Id,
                    PlayerProfileId = null,
                    PickupPalParticipantId = "pp-tope",
                    DisplayName = "tope",
                    IsWaitlist = true,
                    DisplayOrder = 3,
                },
            ]);

        var result = await context.CreateHandler().HandleAsync();

        result!.Roster.Should().HaveCount(3, "the linked waitlister plus both unlinked imports");
        result.Roster.Count(entry => entry.IsWaitlist).Should().Be(3);
        var unlinked = result.Roster.Where(entry => entry.PlayerProfileId is null).ToArray();
        unlinked.Select(entry => entry.DisplayName).Should().Equal("victor", "tope");
        unlinked.Select(entry => entry.PickupPalParticipantId).Should().Equal("pp-victor", "pp-tope");
        unlinked.Should().OnlyContain(entry => !entry.IsCheckedIn);
    }

    [Fact]
    public async Task HandleAsync_WhenImportedParticipantIsLinked_ShowsProfileNameAndLinkedStatus()
    {
        // The bug this covers: after an admin matched "tob8" to Tobi Kareem, the waitlist popup kept
        // showing the WhatsApp handle with "Not linked to a profile" because the roster projected
        // the imported display name instead of the linked profile.
        var context = new TestContext();
        var session = context.SessionAt(Utc(2026, 7, 23, 2, 40));
        var linkedProfileId = Guid.NewGuid();
        context.Clock.SetupGet(x => x.UtcNow).Returns(session.CheckInOpensAtUtc.AddMinutes(1));
        context.ConfigureSession(session);
        context.PickupPalGames
            .Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PickupPalGameParticipant
                {
                    SessionId = session.Id,
                    PlayerProfileId = linkedProfileId,
                    PickupPalParticipantId = "pp-tob8",
                    DisplayName = "tob8",
                    IsWaitlist = true,
                    DisplayOrder = 0,
                },
            ]);

        var result = await context.CreateHandlerWithProfiles(new PlayerProfile
        {
            Id = linkedProfileId,
            DisplayName = "Tobi Kareem",
            PreferredPosition = "Midfielder",
        }).HandleAsync();

        var entry = result!.Roster.Should().ContainSingle().Subject;
        entry.PlayerProfileId.Should().Be(linkedProfileId, "a linked participant is no longer unlinked");
        entry.DisplayName.Should().Be("Tobi Kareem", "the profile name replaces the WhatsApp handle");
        entry.IsWaitlist.Should().BeTrue();
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
            // The display roster names a linked imported participant after its profile, so the
            // handler reads back whichever of these ids it finds linked on the session.
            Profiles
                .Setup(x => x.ListProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    LinkedProfiles.Where(profile => ids.Contains(profile.Id)).ToArray());
            Venues
                .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new Venue { Id = id, Name = "Marina Field" });
            Venues
                .Setup(x => x.ListByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    ids.Select(id => new Venue { Id = id, Name = "Marina Field" }).ToArray());
            // The handler reads attendance for every candidate in one batched call. Fan the batch
            // back out to the per-session stubs so each test keeps expressing attendance one game
            // at a time, and mirror the repository contract of an entry per requested session.
            Rsvps
                .Setup(x => x.GetGameDayAttendanceBatchAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (IReadOnlyCollection<Guid> sessionIds, Guid playerProfileId, CancellationToken token) =>
                {
                    var attendance = new Dictionary<Guid, GameDayAttendanceRecord>(sessionIds.Count);
                    foreach (var sessionId in sessionIds.Distinct())
                    {
                        attendance[sessionId] =
                            await Rsvps.Object.GetGameDayAttendanceAsync(sessionId, playerProfileId, token)
                            ?? new GameDayAttendanceRecord(0, 0, 0, false, false, false, []);
                    }

                    return (IReadOnlyDictionary<Guid, GameDayAttendanceRecord>)attendance;
                });
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
                        Guid.NewGuid(),
                        $"group-{index}@g.us",
                        name,
                        20,
                        index == 0))
                    .ToArray());
        }

        public PlayerProfile Profile { get; } = new()
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Ada"
        };

        /// <summary>Profiles the repository can resolve for linked imported participants.</summary>
        public List<PlayerProfile> LinkedProfiles { get; } = [];

        /// <summary>Snapshot group name per session, for the relevance filter.</summary>
        public Dictionary<Guid, string> GroupNamesBySession { get; } = [];

        /// <summary>WhatsApp groups the current player belongs to.</summary>
        public List<string> PlayerGroupNames { get; } = [];

        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();
        public Mock<IVenueRepository> Venues { get; } = new();
        public Mock<IRsvpRepository> Rsvps { get; } = new();
        public Mock<IPickupPalGameRepository> PickupPalGames { get; } = new();
        public Mock<IPlayerGroupLinkRepository> PlayerGroups { get; } = new();
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

        public GetTodayGameDayContextQueryHandler CreateHandlerWithProfiles(params PlayerProfile[] profiles)
        {
            LinkedProfiles.AddRange(profiles);
            return CreateHandler();
        }

        public GetTodayGameDayContextQueryHandler CreateHandler() => new(
            CurrentUser.Object,
            Clock.Object,
            Profiles.Object,
            Sessions.Object,
            Venues.Object,
            Rsvps.Object,
            PickupPalGames.Object,
            PlayerGroups.Object,
            Stats.Object,
            Eligibility.Object);
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
