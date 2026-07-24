using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;
using DomainMatch = SouthBaySoccer.Domain.Entities.Stats.Match;

namespace SouthBaySoccer.Application.Tests.Stats;

public sealed class MatchStatsSelfServiceHandlerTests
{
    private static readonly Guid IdentityUserId = Guid.NewGuid();
    private static readonly Guid MatchId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_WhenPlayerSubmitsGoalsAndAssists_WritesPendingEventsForSelfOnly()
    {
        var actor = Profile("Ada");
        var stats = StatsRepository(
            match: MatchInStatus(MatchStatus.InProgress),
            assignments: [Assignment(actor.Id, TeamId)]);
        var captured = CaptureOwnPendingReplacement(stats);
        var handler = SubmitHandler(actor, stats);

        await handler.HandleAsync(new SubmitMyMatchStatsCommand(MatchId, Goals: 2, Assists: 1));

        captured.Should().HaveCount(3);
        captured.Should().OnlyContain(x => x.EventType == MatchEventType.Goal
            && x.ReviewStatus == MatchEventReviewStatus.Pending
            && x.SubmittedByPlayerProfileId == actor.Id
            && x.Minute == 0
            && x.MatchTeamId == TeamId);
        captured.Count(x => x.PlayerProfileId == actor.Id && x.AssistPlayerProfileId == null)
            .Should().Be(2, "each claimed goal credits the submitter as scorer");
        captured.Count(x => x.PlayerProfileId == null && x.AssistPlayerProfileId == actor.Id)
            .Should().Be(1, "an assist is a goal credit with no named scorer");
        stats.Verify(x => x.EnsurePlayerMatchParticipationAsync(MatchId, actor.Id, It.IsAny<CancellationToken>()),
            Times.Once, "submitting stats records participation so the leaderboard counts them");
    }

    [Fact]
    public async Task HandleAsync_WhenSubmissionAlreadyConfirmed_ThrowsConflictAndWritesNothing()
    {
        var actor = Profile("Ada");
        var approved = new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = MatchId,
            PlayerProfileId = actor.Id,
            SubmittedByPlayerProfileId = actor.Id,
            EventType = MatchEventType.Goal,
            ReviewStatus = MatchEventReviewStatus.Approved,
        };
        var stats = StatsRepository(match: MatchInStatus(MatchStatus.Completed), events: [approved]);
        var handler = SubmitHandler(actor, stats);

        var act = () => handler.HandleAsync(new SubmitMyMatchStatsCommand(MatchId, Goals: 1, Assists: 0));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        stats.Verify(x => x.ReplaceOwnPendingMatchEventsAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<MatchEvent>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenMatchIsPublished_ThrowsConflict()
    {
        var actor = Profile("Ada");
        var stats = StatsRepository(match: MatchInStatus(MatchStatus.Published));
        var handler = SubmitHandler(actor, stats);

        var act = () => handler.HandleAsync(new SubmitMyMatchStatsCommand(MatchId, Goals: 1, Assists: 0));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task HandleAsync_WhenListingRateableTeammates_IncludesEveryoneWhoAttendedExceptSelf()
    {
        var actor = Profile("Ada");
        var teammate = Profile("Bem");
        var opponent = Profile("Chi");
        var handler = RateableHandler(actor, [actor, teammate, opponent], out _);

        var result = await handler.HandleAsync(new GetRateableTeammatesQuery(MatchId));

        result.Select(x => x.PlayerProfileId)
            .Should().BeEquivalentTo([teammate.Id, opponent.Id],
                "everyone who played is rateable, not just the rater's own side");
        result.Should().NotContain(x => x.PlayerProfileId == actor.Id, "INV-8 forbids a self vote");
    }

    [Fact]
    public async Task HandleAsync_WhenPlayerDidNotAttend_ReturnsNoRateableTeammates()
    {
        var actor = Profile("Ada");
        var other = Profile("Bem");
        var handler = RateableHandler(actor, [other], out _);

        var result = await handler.HandleAsync(new GetRateableTeammatesQuery(MatchId));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenRatingWindowHasClosed_ReturnsNoRateableTeammates()
    {
        var actor = Profile("Ada");
        var teammate = Profile("Bem");
        // Four days after kick-off is outside the three-day peer-feedback window.
        var handler = RateableHandler(actor, [actor, teammate], out _, kickOffOffset: TimeSpan.FromDays(-4));

        var result = await handler.HandleAsync(new GetRateableTeammatesQuery(MatchId));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenCaptainConfirmsSubmission_ApprovesOnlyThatPlayersPendingRows()
    {
        var captain = Profile("Ada");
        var submitter = Profile("Bem");
        var mine = PendingEvent(submitter.Id);
        var alsoMine = PendingEvent(submitter.Id);
        var someoneElse = PendingEvent(Guid.NewGuid());
        var stats = StatsRepository(
            match: MatchInStatus(MatchStatus.Completed),
            events: [mine, alsoMine, someoneElse],
            teams: [new MatchTeam { Id = TeamId, MatchId = MatchId, CaptainPlayerProfileId = captain.Id }]);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 7, 22, 3, 0, 0, DateTimeKind.Utc));
        var handler = new ConfirmPlayerSubmissionCommandHandler(
            CurrentUser().Object,
            clock.Object,
            new ConfirmPlayerSubmissionCommandValidator(),
            ProfileRepository(captain).Object,
            stats.Object,
            Mock.Of<IUnitOfWork>());

        var result = await handler.HandleAsync(new ConfirmPlayerSubmissionCommand(MatchId, submitter.Id));

        result.AffectedCount.Should().Be(2);
        mine.ReviewStatus.Should().Be(MatchEventReviewStatus.Approved);
        alsoMine.ReviewStatus.Should().Be(MatchEventReviewStatus.Approved);
        mine.ReviewedByPlayerProfileId.Should().Be(captain.Id);
        someoneElse.ReviewStatus.Should().Be(MatchEventReviewStatus.Pending, "another player's claim is untouched");
    }

    [Fact]
    public async Task HandleAsync_WhenReadingOwnStats_CountsGoalsAndAssistsFromRawEvents()
    {
        var actor = Profile("Ada");
        var events = new[]
        {
            Goal(actor.Id, scorer: actor.Id, assist: null),
            Goal(actor.Id, scorer: actor.Id, assist: null),
            Goal(actor.Id, scorer: null, assist: actor.Id),
        };
        var stats = StatsRepository(match: MatchInStatus(MatchStatus.InProgress), events: events);
        var sessions = new Mock<ISessionRepository>();
        var handler = new GetMyMatchStatsQueryHandler(
            CurrentUser().Object,
            ProfileRepository(actor).Object,
            stats.Object,
            sessions.Object,
            Mock.Of<IVenueRepository>());

        var result = await handler.HandleAsync(new GetMyMatchStatsQuery(MatchId));

        result.Goals.Should().Be(2);
        result.Assists.Should().Be(1);
        result.IsPendingConfirmation.Should().BeTrue();
        result.CanSubmit.Should().BeTrue();
        result.TeammateSubmissions.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenRecentMatchAwaitsMyStats_ReturnsPromptForThatMatch()
    {
        var actor = Profile("Ada");
        var handler = PendingSubmissionHandler(actor, out _, onRoster: true, events: []);

        var result = await handler.HandleAsync(new GetPendingStatSubmissionQuery());

        result.Should().NotBeNull();
        result!.MatchId.Should().Be(MatchId);
        result.IsPendingConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenMyStatsAreAlreadyConfirmed_ReturnsNoPrompt()
    {
        var actor = Profile("Ada");
        var approved = Goal(actor.Id, scorer: actor.Id, assist: null);
        approved.ReviewStatus = MatchEventReviewStatus.Approved;
        var handler = PendingSubmissionHandler(actor, out _, onRoster: true, events: [approved]);

        var result = await handler.HandleAsync(new GetPendingStatSubmissionQuery());

        result.Should().BeNull("a confirmed tally changes only through a stat correction");
    }

    [Fact]
    public async Task HandleAsync_WhenPlayerWasNotOnTheRoster_ReturnsNoPrompt()
    {
        var actor = Profile("Ada");
        var handler = PendingSubmissionHandler(actor, out _, onRoster: false, events: []);

        var result = await handler.HandleAsync(new GetPendingStatSubmissionQuery());

        result.Should().BeNull();
    }

    private static GetPendingStatSubmissionQueryHandler PendingSubmissionHandler(
        PlayerProfile actor,
        out Mock<IStatsRepository> stats,
        bool onRoster,
        MatchEvent[] events)
    {
        // Kick-off two hours ago: post-game is open and we are inside the submission window.
        var nowUtc = new DateTime(2026, 7, 23, 4, 0, 0, DateTimeKind.Utc);
        var session = new Session
        {
            Id = Guid.NewGuid(),
            Title = "Marina Field - Thursday pickup",
            StartsAtUtc = nowUtc.AddHours(-2),
            CheckInOpensAtUtc = nowUtc.AddHours(-2).AddMinutes(-30),
            CheckInClosesAtUtc = nowUtc.AddHours(-2),
            Status = SessionStatus.Published,
        };
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);
        var sessions = new Mock<ISessionRepository>();
        sessions
            .Setup(x => x.ListGameDayCandidatesAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        var rsvps = new Mock<IRsvpRepository>();
        rsvps
            .Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(onRoster
                ? [new RosterMemberRecord(actor.Id, actor.DisplayName, string.Empty, false, null)]
                : []);
        rsvps
            .Setup(x => x.ListActiveWaitlistRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var games = new Mock<IPickupPalGameRepository>();
        games
            .Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        stats = new Mock<IStatsRepository>();
        stats
            .Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainMatch { Id = MatchId, SessionId = session.Id, Status = MatchStatus.InProgress });
        stats
            .Setup(x => x.ListMatchEventsAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        return new GetPendingStatSubmissionQueryHandler(
            CurrentUser().Object,
            clock.Object,
            ProfileRepository(actor).Object,
            sessions.Object,
            rsvps.Object,
            games.Object,
            stats.Object);
    }

    private static GetRateableTeammatesQueryHandler RateableHandler(
        PlayerProfile actor,
        PlayerProfile[] roster,
        out Mock<IStatsRepository> stats,
        TimeSpan? kickOffOffset = null)
    {
        var nowUtc = new DateTime(2026, 7, 23, 4, 0, 0, DateTimeKind.Utc);
        var session = new Session
        {
            Id = Guid.NewGuid(),
            Title = "Marina Field - Thursday pickup",
            StartsAtUtc = nowUtc.Add(kickOffOffset ?? TimeSpan.FromHours(-2)),
            Status = SessionStatus.Published,
        };
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var rsvps = new Mock<IRsvpRepository>();
        rsvps
            .Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster
                .Select(p => new RosterMemberRecord(p.Id, p.DisplayName, string.Empty, false, null))
                .ToArray());
        rsvps
            .Setup(x => x.ListActiveWaitlistRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var games = new Mock<IPickupPalGameRepository>();
        games
            .Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        stats = new Mock<IStatsRepository>();
        stats
            .Setup(x => x.FindMatchAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainMatch { Id = MatchId, SessionId = session.Id, Status = MatchStatus.InProgress });
        stats
            .Setup(x => x.ListMatchEventsAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var profiles = ProfileRepository(actor);
        profiles
            .Setup(x => x.ListProfilesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                roster.Where(p => ids.Contains(p.Id)).ToArray());

        return new GetRateableTeammatesQueryHandler(
            CurrentUser().Object,
            clock.Object,
            profiles.Object,
            sessions.Object,
            rsvps.Object,
            games.Object,
            stats.Object);
    }

    [Fact]
    public async Task PendingSubmission_WhenPlayerNotOnRosterButEntriesUnclaimed_PromptsToClaimFirst()
    {
        var actor = Profile("Vic");
        var model = await PendingSubmissionAsync(actor, onRoster: false, hasUnclaimed: true);

        model.Should().NotBeNull();
        model!.RequiresClaim.Should().BeTrue();
        model.MatchId.Should().Be(Guid.Empty);
        model.SessionId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task PendingSubmission_WhenPlayerOnRoster_PromptsToSubmitTheMatch()
    {
        var actor = Profile("Vic");
        var model = await PendingSubmissionAsync(actor, onRoster: true, hasUnclaimed: false);

        model.Should().NotBeNull();
        model!.RequiresClaim.Should().BeFalse();
        model.MatchId.Should().Be(MatchId);
    }

    [Fact]
    public async Task PendingSubmission_WhenNotOnRosterAndNothingUnclaimed_ReturnsNull()
    {
        var actor = Profile("Vic");
        var model = await PendingSubmissionAsync(actor, onRoster: false, hasUnclaimed: false);

        model.Should().BeNull();
    }

    private static async Task<PendingStatSubmissionModel?> PendingSubmissionAsync(
        PlayerProfile actor,
        bool onRoster,
        bool hasUnclaimed)
    {
        var nowUtc = new DateTime(2026, 7, 23, 4, 0, 0, DateTimeKind.Utc);
        var session = new SouthBaySoccer.Domain.Entities.Scheduling.Session
        {
            Id = Guid.NewGuid(),
            Title = "Bay Area Soccer - Wednesday pickup",
            StartsAtUtc = nowUtc.AddHours(-2),
            Status = SouthBaySoccer.Domain.Enumerations.SessionStatus.Published,
        };
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(nowUtc);
        var sessions = new Mock<ISessionRepository>();
        sessions.Setup(x => x.ListGameDayCandidatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([session]);
        var rsvps = new Mock<IRsvpRepository>();
        rsvps.Setup(x => x.ListGoingRosterAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(onRoster
                ? [new RosterMemberRecord(actor.Id, actor.DisplayName, string.Empty, false, null)]
                : []);
        rsvps.Setup(x => x.ListActiveWaitlistRosterAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var games = new Mock<IPickupPalGameRepository>();
        games.Setup(x => x.ListParticipantsAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasUnclaimed
                ? [new PickupPalGameParticipant { Id = Guid.NewGuid(), SessionId = session.Id, DisplayName = "victor", PlayerProfileId = null }]
                : []);
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindPrimaryMatchBySessionAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainMatch { Id = MatchId, SessionId = session.Id, Status = MatchStatus.InProgress });
        stats.Setup(x => x.ListMatchEventsAsync(MatchId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var handler = new GetPendingStatSubmissionQueryHandler(
            CurrentUser().Object,
            clock.Object,
            ProfileRepository(actor).Object,
            sessions.Object,
            rsvps.Object,
            games.Object,
            stats.Object);

        return await handler.HandleAsync(new GetPendingStatSubmissionQuery());
    }

    private static PlayerProfile Profile(string name) =>
        new() { Id = Guid.NewGuid(), IdentityUserId = IdentityUserId, DisplayName = name };

    private static DomainMatch MatchInStatus(MatchStatus status) =>
        new() { Id = MatchId, SessionId = Guid.NewGuid(), Status = status };

    private static TeamAssignment Assignment(Guid playerProfileId, Guid matchTeamId) =>
        new() { Id = Guid.NewGuid(), MatchId = MatchId, MatchTeamId = matchTeamId, PlayerProfileId = playerProfileId };

    private static MatchEvent PendingEvent(Guid submittedById) =>
        Goal(submittedById, scorer: submittedById, assist: null);

    private static MatchEvent Goal(Guid submittedById, Guid? scorer, Guid? assist) =>
        new()
        {
            Id = Guid.NewGuid(),
            MatchId = MatchId,
            PlayerProfileId = scorer,
            AssistPlayerProfileId = assist,
            EventType = MatchEventType.Goal,
            SubmittedByPlayerProfileId = submittedById,
            ReviewStatus = MatchEventReviewStatus.Pending,
        };

    private static Mock<ICurrentUser> CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(IdentityUserId);
        return currentUser;
    }

    private static Mock<IPlayerProfileRepository> ProfileRepository(PlayerProfile actor)
    {
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(IdentityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor);
        profiles.Setup(x => x.ListProfilesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return profiles;
    }

    private static Mock<IStatsRepository> StatsRepository(
        DomainMatch match,
        MatchEvent[]? events = null,
        TeamAssignment[]? assignments = null,
        MatchTeam[]? teams = null)
    {
        var stats = new Mock<IStatsRepository>();
        stats.Setup(x => x.FindMatchAsync(MatchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
        stats.Setup(x => x.ListMatchEventsAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events ?? []);
        stats.Setup(x => x.ListAssignmentsAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments ?? [Assignment(Guid.Empty, TeamId)]);
        stats.Setup(x => x.ListMatchTeamsAsync(MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams ?? []);
        return stats;
    }

    private static List<MatchEvent> CaptureOwnPendingReplacement(Mock<IStatsRepository> stats)
    {
        var captured = new List<MatchEvent>();
        stats.Setup(x => x.ReplaceOwnPendingMatchEventsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<MatchEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, IReadOnlyList<MatchEvent> events, CancellationToken _) =>
                captured.AddRange(events))
            .Returns(Task.CompletedTask);
        return captured;
    }

    private static SubmitMyMatchStatsCommandHandler SubmitHandler(
        PlayerProfile actor,
        Mock<IStatsRepository> stats) =>
        new(
            CurrentUser().Object,
            new SubmitMyMatchStatsCommandValidator(),
            ProfileRepository(actor).Object,
            stats.Object,
            Mock.Of<IUnitOfWork>());
}
