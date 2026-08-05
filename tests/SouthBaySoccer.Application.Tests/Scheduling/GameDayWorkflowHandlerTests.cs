using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Application.Features.Stats;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Entities.Stats;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;
using MatchEntity = SouthBaySoccer.Domain.Entities.Stats.Match;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class GameDayWorkflowHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenConfirmedCaptainsAreValid_CreatesAuditedTopology()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var secondCaptainId = Guid.NewGuid();
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(secondCaptainId, "Grace White")
            ]);
        var handler = new AssignSessionCaptainsCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new AssignSessionCaptainsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new AssignSessionCaptainsCommand(
            context.Session.Id,
            2,
            [context.Actor.Id, secondCaptainId]));

        result.AffectedCount.Should().Be(2);
        context.Stats.Verify(x => x.CreateMatchAsync(
            It.Is<MatchEntity>(match => match.SessionId == context.Session.Id && match.Status == MatchStatus.Draft),
            It.Is<IReadOnlyList<MatchTeam>>(teams => teams.Count == 2
                && teams.All(team => team.CaptainPlayerProfileId.HasValue)),
            It.Is<IReadOnlyList<TeamAssignment>>(assignments => assignments.Count == 2),
            It.Is<IReadOnlyList<PlayerMatchStats>>(participants => participants.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "Session.Captains.Assign"
                && entry.ActorPlayerProfileId == context.Actor.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        context.UnitOfWork.Verify(
            x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GameDayMutationModel>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "draft mutations commit through the serializable transaction");
    }

    [Fact]
    public async Task HandleAsync_WhenWaitlistedPlayerIsCaptain_CreatesTopology()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var waitlistCaptainId = Guid.NewGuid();
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Roster(context.Actor.Id, "Ada Green")]);
        context.Rsvps
            .Setup(x => x.ListActiveWaitlistRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RosterMemberRecord(waitlistCaptainId, "Wade Waitlist", "FWD", false, 1)]);
        var handler = new AssignSessionCaptainsCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new AssignSessionCaptainsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new AssignSessionCaptainsCommand(
            context.Session.Id,
            2,
            [context.Actor.Id, waitlistCaptainId]));

        result.AffectedCount.Should().Be(2);
        context.Stats.Verify(x => x.CreateMatchAsync(
            It.IsAny<MatchEntity>(),
            It.Is<IReadOnlyList<MatchTeam>>(teams => teams.Count == 2),
            It.IsAny<IReadOnlyList<TeamAssignment>>(),
            It.IsAny<IReadOnlyList<PlayerMatchStats>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCaptainIsNotConfirmed_RejectsWithoutMutation()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Roster(context.Actor.Id, "Ada Green")]);
        var handler = new AssignSessionCaptainsCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new AssignSessionCaptainsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new AssignSessionCaptainsCommand(
            context.Session.Id,
            2,
            [context.Actor.Id, Guid.NewGuid()]));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*confirmed*");
        context.Stats.Verify(x => x.CreateMatchAsync(
            It.IsAny<MatchEntity>(),
            It.IsAny<IReadOnlyList<MatchTeam>>(),
            It.IsAny<IReadOnlyList<TeamAssignment>>(),
            It.IsAny<IReadOnlyList<PlayerMatchStats>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerCaptainsAnotherTeam_RejectsDraftMutation()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var otherCaptainId = Guid.NewGuid();
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(otherCaptainId, 2);
        context.ConfigureMatch([actorTeam, otherTeam]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Roster(context.Actor.Id, "Ada Green")]);
        var handler = new SaveCaptainTeamPicksCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SaveCaptainTeamPicksCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new SaveCaptainTeamPicksCommand(
            context.Session.Id,
            otherTeam.Id,
            [context.Actor.Id]));

        // Since the snake draft (TEAM-5), the bulk replace is the admin's correction tool only;
        // captains draft one pick at a time on their turn.
        await act.Should().ThrowAsync<ApplicationForbiddenException>()
            .WithMessage("*game admin*");
        context.Stats.Verify(x => x.ReplaceTeamAssignmentsAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenGameAdminDraftsAnotherCaptainsTeam_SavesPicks()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var captainId = Guid.NewGuid();
        var pickId = Guid.NewGuid();
        var team = context.Team(captainId, 1);
        context.ConfigureMatch([team, context.Team(Guid.NewGuid(), 2)]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(captainId, "Cap One"),
                context.Roster(pickId, "Pick One"),
                context.Roster(Guid.NewGuid(), "Bench One"),
                context.Roster(Guid.NewGuid(), "Bench Two")
            ]);
        var handler = new SaveCaptainTeamPicksCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SaveCaptainTeamPicksCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new SaveCaptainTeamPicksCommand(
            context.Session.Id,
            team.Id,
            [pickId]));

        result.AffectedCount.Should().Be(2);
        context.Stats.Verify(x => x.ReplaceTeamAssignmentsAsync(
            context.Match.Id,
            team.Id,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(pickId) && ids.Contains(captainId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherTeamAlreadyPickedPlayer_RejectsDraftMutation()
    {
        // Admin path: bulk save is admin-only since TEAM-5, and the cross-team conflict guard
        // still applies to admins.
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var otherCaptainId = Guid.NewGuid();
        var selectedPlayerId = Guid.NewGuid();
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(otherCaptainId, 2);
        context.ConfigureMatch([actorTeam, otherTeam]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Assignment(actorTeam.Id, context.Actor.Id),
                context.Assignment(otherTeam.Id, selectedPlayerId)
            ]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(selectedPlayerId, "Picked Player"),
                context.Roster(Guid.NewGuid(), "Bench One"),
                context.Roster(Guid.NewGuid(), "Bench Two")
            ]);
        var handler = new SaveCaptainTeamPicksCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SaveCaptainTeamPicksCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new SaveCaptainTeamPicksCommand(
            context.Session.Id,
            actorTeam.Id,
            [selectedPlayerId]));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*already been drafted*");
    }

    [Fact]
    public async Task HandleAsync_WhenDraftWindowHasEnded_LocksValidTeamsForPostGame()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var otherCaptainId = Guid.NewGuid();
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(otherCaptainId, 2);
        var assignments = new[]
        {
            context.Assignment(actorTeam.Id, context.Actor.Id),
            context.Assignment(otherTeam.Id, otherCaptainId),
        };
        context.ConfigureMatch([actorTeam, otherTeam]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(otherCaptainId, "Grace White")
            ]);
        var handler = new LockSessionTeamsCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new LockSessionTeamsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new LockSessionTeamsCommand(context.Session.Id));

        result.AffectedCount.Should().Be(1);
        context.Match.Status.Should().Be(MatchStatus.InProgress);
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "TeamDraft.Lock"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenPendingEventIsApproved_UpdatesExactEvent()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(Guid.NewGuid(), 2);
        var matchEvent = new MatchEvent
        {
            Id = Guid.NewGuid(),
            MatchId = context.Match.Id,
            PlayerProfileId = context.Actor.Id,
            EventType = MatchEventType.Goal,
            ReviewStatus = MatchEventReviewStatus.Pending,
        };
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.InProgress);
        context.Stats
            .Setup(x => x.FindMatchEventAsync(matchEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchEvent);
        var reviewHandler = new ReviewMatchEventCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new ReviewMatchEventCommandValidator(),
            context.Profiles.Object,
            context.Stats.Object,
            context.UnitOfWork.Object);
        var handler = new ApprovePostGameStatCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new ApprovePostGameStatCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Stats.Object,
            reviewHandler);

        var result = await handler.HandleAsync(new ApprovePostGameStatCommand(context.Session.Id, matchEvent.Id));

        result.AffectedCount.Should().Be(1);
        matchEvent.ReviewStatus.Should().Be(MatchEventReviewStatus.Approved);
        matchEvent.ReviewedByPlayerProfileId.Should().Be(context.Actor.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenFinalTeamResultContradictsRotation_MarksMatchForReview()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(Guid.NewGuid(), 2);
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.InProgress);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Result(otherTeam.Id, wins: 1)]);
        var handler = new SavePostGameTeamResultCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SavePostGameTeamResultCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new SavePostGameTeamResultCommand(
            context.Session.Id,
            actorTeam.Id,
            1,
            0,
            0));

        result.AffectedCount.Should().Be(1);
        context.Match.Status.Should().Be(MatchStatus.NeedsReview);
        context.Stats.Verify(x => x.AddStatCorrectionAsync(
            It.Is<StatCorrection>(correction => correction.Reason == "Conflicting team result submissions."),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAdminRecordsResultOnDraftMatch_AutoLocksThenRecords()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var otherCaptainId = Guid.NewGuid();
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(otherCaptainId, 2);
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.Draft);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Assignment(actorTeam.Id, context.Actor.Id),
                context.Assignment(otherTeam.Id, otherCaptainId)
            ]);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new SavePostGameTeamResultCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SavePostGameTeamResultCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var result = await handler.HandleAsync(new SavePostGameTeamResultCommand(context.Session.Id, actorTeam.Id, 1, 0, 0));

        context.Match.Status.Should().Be(MatchStatus.InProgress);
        result.AffectedCount.Should().Be(1);
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "TeamDraft.Lock.OnResult"),
            It.IsAny<CancellationToken>()), Times.Once);
        context.Stats.Verify(x => x.UpsertMatchResultsAsync(
            context.Match.Id, It.IsAny<IReadOnlyList<MatchResult>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenCaptainRecordsResultOnDraftMatch_IsRejected()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(Guid.NewGuid(), 2);
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.Draft);
        var handler = new SavePostGameTeamResultCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SavePostGameTeamResultCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new SavePostGameTeamResultCommand(context.Session.Id, actorTeam.Id, 1, 0, 0));

        await act.Should().ThrowAsync<ApplicationConflictException>().WithMessage("*game admin*");
        context.Match.Status.Should().Be(MatchStatus.Draft);
    }

    [Fact]
    public async Task HandleAsync_WhenAnEventIsPending_RejectsPublish()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(Guid.NewGuid(), 2);
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.Completed);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Result(actorTeam.Id, wins: 1),
                context.Result(otherTeam.Id, losses: 1)
            ]);
        context.Stats
            .Setup(x => x.ListMatchEventsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchEvent
            {
                Id = Guid.NewGuid(),
                MatchId = context.Match.Id,
                EventType = MatchEventType.Goal,
                ReviewStatus = MatchEventReviewStatus.Pending,
            }]);
        var handler = context.CreatePublishHandler();

        var act = () => handler.HandleAsync(new PublishPostGameCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*must be reviewed*");
        context.Match.Status.Should().Be(MatchStatus.Completed);
    }

    [Fact]
    public async Task HandleAsync_WhenBulkSaveExceedsTheTeamCap_RejectsWithTheCap()
    {
        // The admin correction tool obeys the same server-owned caps as the draft: 4 players over
        // 2 teams caps each side at 2, so captain + 2 picks must be refused.
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var captainId = Guid.NewGuid();
        var pickOne = Guid.NewGuid();
        var pickTwo = Guid.NewGuid();
        var team = context.Team(captainId, 1);
        context.ConfigureMatch([team, context.Team(Guid.NewGuid(), 2)]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(captainId, "Cap One"),
                context.Roster(pickOne, "Pick One"),
                context.Roster(pickTwo, "Pick Two"),
                context.Roster(Guid.NewGuid(), "Bench One")
            ]);
        var handler = new SaveCaptainTeamPicksCommandHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            new SaveCaptainTeamPicksCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

        var act = () => handler.HandleAsync(new SaveCaptainTeamPicksCommand(
            context.Session.Id,
            team.Id,
            [pickOne, pickTwo]));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*at most 2 players*");
        context.Stats.Verify(x => x.ReplaceTeamAssignmentsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ResolveDraftTurn_WhenATeamIsOverConsumed_StillHandsTheTurnToUnderCapTeams()
    {
        // Roster churn (an RSVP flipping mid-draft) can shrink caps below counts already recorded;
        // the replay must treat the over-full team as done rather than stalling or looping.
        var teams = new[]
        {
            new MatchTeam { Id = Guid.NewGuid(), TeamNumber = 1, CaptainPlayerProfileId = Guid.NewGuid() },
            new MatchTeam { Id = Guid.NewGuid(), TeamNumber = 2, CaptainPlayerProfileId = Guid.NewGuid() },
        };

        var (onTheClock, _) = GameDayWorkflowQueries.ResolveDraftTurn(teams, [2, 2], [3, 0]);

        onTheClock.Should().Be(teams[1].Id, "the only team still under its cap is on the clock");

        var (complete, _) = GameDayWorkflowQueries.ResolveDraftTurn(teams, [2, 2], [3, 1]);
        complete.Should().BeNull("every under-cap slot is consumed");
    }

    [Fact]
    public async Task HandleAsync_WhenResultsAndReviewsAreComplete_PublishesAndAuditsMatch()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(Guid.NewGuid(), 2);
        context.ConfigureMatch([actorTeam, otherTeam], MatchStatus.Completed);
        context.Stats
            .Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Result(actorTeam.Id, wins: 1),
                context.Result(otherTeam.Id, losses: 1)
            ]);
        context.Stats
            .Setup(x => x.ListMatchEventsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MatchEvent
            {
                Id = Guid.NewGuid(),
                MatchId = context.Match.Id,
                EventType = MatchEventType.Goal,
                ReviewStatus = MatchEventReviewStatus.Approved,
            }]);
        var handler = context.CreatePublishHandler();

        var result = await handler.HandleAsync(new PublishPostGameCommand(context.Session.Id));

        result.AffectedCount.Should().Be(1);
        context.Match.Status.Should().Be(MatchStatus.Published);
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "PostGame.Publish"),
            It.IsAny<CancellationToken>()), Times.Once);
        // Publish is a post-game mutation, not a draft one — it commits directly.
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unlock_WhenInProgressWithNoResults_RevertsToDraft()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.InProgress);
        context.Stats.Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        context.Stats.Setup(x => x.ListMatchEventsAsync(context.Match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var handler = CreateUnlockHandler(context);

        var result = await handler.HandleAsync(new UnlockSessionTeamsCommand(context.Session.Id));

        result.AffectedCount.Should().Be(1);
        context.Match.Status.Should().Be(MatchStatus.Draft);
        context.Match.StartedAtUtc.Should().BeNull();
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "TeamDraft.Unlock"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unlock_WhenAlreadyDraft_IsNoOp()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.Draft);
        var handler = CreateUnlockHandler(context);

        var result = await handler.HandleAsync(new UnlockSessionTeamsCommand(context.Session.Id));

        result.AffectedCount.Should().Be(0);
        context.Match.Status.Should().Be(MatchStatus.Draft);
    }

    [Fact]
    public async Task Unlock_WhenResultsRecorded_ThrowsAndLeavesMatchInProgress()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.InProgress);
        context.Stats.Setup(x => x.ListMatchResultsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Result(team.Id, wins: 1)]);
        context.Stats.Setup(x => x.ListMatchEventsAsync(context.Match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var handler = CreateUnlockHandler(context);

        var act = () => handler.HandleAsync(new UnlockSessionTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>();
        context.Match.Status.Should().Be(MatchStatus.InProgress);
    }

    [Fact]
    public async Task Unlock_WhenCompleted_ThrowsPointingToPostGameReopen()
    {
        var context = new TestContext(postGame: true, isGameAdmin: true);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.Completed);
        var handler = CreateUnlockHandler(context);

        var act = () => handler.HandleAsync(new UnlockSessionTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    [Fact]
    public async Task Unlock_WhenNotGameAdmin_IsForbidden()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.InProgress);
        var handler = CreateUnlockHandler(context);

        var act = () => handler.HandleAsync(new UnlockSessionTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    [Fact]
    public async Task GetSessionTeams_ForRosteredPlayer_ReturnsTeamsWithTheirTeamMarked()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var team = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([team], MatchStatus.InProgress);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Roster(context.Actor.Id, "Ada Green")]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Assignment(team.Id, context.Actor.Id)]);
        var handler = CreateSessionTeamsHandler(context);

        var result = await handler.HandleAsync(context.Session.Id);

        result.Teams.Should().ContainSingle();
        result.Teams[0].IsMine.Should().BeTrue();
        result.Teams[0].Members.Should().ContainSingle(member => member.IsMe && member.IsCaptain);
        result.IsDraftInProgress.Should().BeFalse("the match has left Draft");
        result.AvailablePlayers.Should().BeNull();
    }

    [Fact]
    public async Task GetSessionTeams_MidDraft_ReportsTurnAndPlayersYetToBePicked()
    {
        // The player's live draft view: partial sheets are labelled with whose turn it is, and the
        // remaining Going/Waitlist pool is listed so watching the draft makes sense.
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var captainId = Guid.NewGuid();
        var team = context.Team(captainId, 1);
        var undraftedId = Guid.NewGuid();
        context.ConfigureMatch([team], MatchStatus.Draft);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(captainId, "Vic Green"),
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(undraftedId, "Bench Player"),
            ]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Assignment(team.Id, captainId)]);
        var handler = CreateSessionTeamsHandler(context);

        var result = await handler.HandleAsync(context.Session.Id);

        result.IsDraftInProgress.Should().BeTrue();
        result.OnTheClockLabel.Should().Be("On the clock: Team Green");
        result.AvailablePlayers.Should().NotBeNull();
        result.AvailablePlayers!.Select(player => player.DisplayName)
            .Should().Equal("Ada Green", "Bench Player");
        result.AvailablePlayers!.Single(player => player.PlayerProfileId == context.Actor.Id)
            .IsMe.Should().BeTrue();
    }

    [Fact]
    public async Task GetSessionTeams_ForPlayerNotOnRoster_IsForbidden()
    {
        var context = new TestContext(postGame: true, isGameAdmin: false);
        var handler = CreateSessionTeamsHandler(context);

        var act = () => handler.HandleAsync(context.Session.Id);

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
    }

    private static UnlockSessionTeamsCommandHandler CreateUnlockHandler(TestContext context) =>
        new(
            context.CurrentUser.Object,
            context.Clock.Object,
            new UnlockSessionTeamsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

    private static GetSessionTeamsQueryHandler CreateSessionTeamsHandler(TestContext context) =>
        new(
            context.CurrentUser.Object,
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object);

    [Fact]
    public async Task DraftPick_WhenOnTheClockCaptainPicks_AssignsToTheirTeam()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var otherCaptainId = Guid.NewGuid();
        var actorTeam = context.Team(context.Actor.Id, 1);
        var otherTeam = context.Team(otherCaptainId, 2);
        var pickId = Guid.NewGuid();
        context.ConfigureMatch([actorTeam, otherTeam]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(otherCaptainId, "Vic White"),
                context.Roster(pickId, "New Pick"),
                context.Roster(Guid.NewGuid(), "Bench Player"),
            ]);

        // 4 eligible / 2 teams => caps 2/2; no picks yet => team 1 (the actor's) is on the clock.
        var result = await CreateDraftPickHandler(context)
            .HandleAsync(new DraftPickCommand(context.Session.Id, pickId));

        result.AffectedCount.Should().Be(1);
        context.Stats.Verify(x => x.ReplaceTeamAssignmentsAsync(
            context.Match.Id,
            actorTeam.Id,
            It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(pickId) && ids.Contains(context.Actor.Id)),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task DraftPick_WhenNotYourTurn_RejectsWithTeamOnTheClock()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var firstCaptainId = Guid.NewGuid();
        var benchId = Guid.NewGuid();
        var firstTeam = context.Team(firstCaptainId, 1);
        var actorTeam = context.Team(context.Actor.Id, 2);
        context.ConfigureMatch([firstTeam, actorTeam]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(firstCaptainId, "Vic Green"),
                context.Roster(context.Actor.Id, "Ada White"),
                context.Roster(benchId, "Bench One"),
                context.Roster(Guid.NewGuid(), "Bench Two"),
            ]);

        var act = () => CreateDraftPickHandler(context)
            .HandleAsync(new DraftPickCommand(context.Session.Id, benchId));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*Team Green is on the clock*");
    }

    [Fact]
    public async Task DraftPick_WhenPlayerAlreadyDrafted_Rejects()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        var takenId = Guid.NewGuid();
        context.ConfigureMatch([actorTeam]);
        context.Stats
            .Setup(x => x.ListAssignmentsAsync(context.Match.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Assignment(actorTeam.Id, takenId)]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(context.Actor.Id, "Ada Green"),
                context.Roster(takenId, "Taken Player"),
                context.Roster(Guid.NewGuid(), "Bench"),
            ]);

        var act = () => CreateDraftPickHandler(context)
            .HandleAsync(new DraftPickCommand(context.Session.Id, takenId));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*already been drafted*");
    }

    [Fact]
    public async Task AutoBalance_WhenNotGameAdmin_IsForbidden()
    {
        // Captains included: one captain must not be able to erase every other captain's picks.
        var context = new TestContext(postGame: false, isGameAdmin: false);
        var actorTeam = context.Team(context.Actor.Id, 1);
        context.ConfigureMatch([actorTeam, context.Team(Guid.NewGuid(), 2)]);

        var act = () => CreateAutoBalanceHandler(context)
            .HandleAsync(new AutoBalanceTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationForbiddenException>();
        context.Stats.Verify(
            x => x.ReplaceAllTeamAssignmentsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(MatchStatus.InProgress)]
    [InlineData(MatchStatus.Completed)]
    [InlineData(MatchStatus.NeedsReview)]
    public async Task AutoBalance_WhenMatchLeftDraft_Rejects(MatchStatus status)
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        context.ConfigureMatch([context.Team(Guid.NewGuid(), 1), context.Team(Guid.NewGuid(), 2)], status);

        var act = () => CreateAutoBalanceHandler(context)
            .HandleAsync(new AutoBalanceTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*still a draft*");
    }

    [Fact]
    public async Task AutoBalance_WhenAdminOnDraftMatch_DealsEveryEligiblePlayerOnce()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();
        var teamA = context.Team(captainA, 1);
        var teamB = context.Team(captainB, 2);
        context.ConfigureMatch([teamA, teamB]);
        var roster = new[] { captainA, captainB }
            .Concat(Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()))
            .ToArray();
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster.Select((id, index) => context.Roster(id, $"Player {index}")).ToArray());
        context.Stats
            .Setup(x => x.ListPlayerRatingAggregatesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlayerRatingAggregateRecord(roster[2], 27m, 3)]);
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? written = null;
        context.Stats
            .Setup(x => x.ReplaceAllTeamAssignmentsAsync(
                context.Match.Id,
                It.IsAny<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid _, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> deal, CancellationToken _) => written = deal)
            .Returns(Task.CompletedTask);

        var result = await CreateAutoBalanceHandler(context)
            .HandleAsync(new AutoBalanceTeamsCommand(context.Session.Id));

        result.AffectedCount.Should().Be(9);
        written.Should().NotBeNull();
        // 9 eligible / 2 teams => caps 5/4 with the extra on the 1st-ranked team.
        written![teamA.Id].Should().HaveCount(5).And.Contain(captainA);
        written[teamB.Id].Should().HaveCount(4).And.Contain(captainB);
        written.Values.SelectMany(ids => ids).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(roster);
        context.Audits.Verify(x => x.AddAsync(
            It.Is<AuditLogEntry>(entry => entry.Action == "TeamDraft.AutoBalance"),
            It.IsAny<CancellationToken>()));
        context.UnitOfWork.Verify(
            x => x.ExecuteInSerializableTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<GameDayMutationModel>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "draft mutations commit through the serializable transaction");
    }

    [Fact]
    public async Task AutoBalance_WhenCaptainNoLongerEligible_Rejects()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        var teamA = context.Team(Guid.NewGuid(), 1);
        var teamB = context.Team(Guid.NewGuid(), 2);
        context.ConfigureMatch([teamA, teamB]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                context.Roster(teamA.CaptainPlayerProfileId!.Value, "Captain A"),
                context.Roster(Guid.NewGuid(), "Someone Else"),
            ]);

        var act = () => CreateAutoBalanceHandler(context)
            .HandleAsync(new AutoBalanceTeamsCommand(context.Session.Id));

        await act.Should().ThrowAsync<ApplicationConflictException>()
            .WithMessage("*Not enough eligible players*");
    }

    private static DraftPickCommandHandler CreateDraftPickHandler(TestContext context) =>
        new(
            context.CurrentUser.Object,
            context.Clock.Object,
            new DraftPickCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

    private static AutoBalanceTeamsCommandHandler CreateAutoBalanceHandler(TestContext context) =>
        new(
            context.CurrentUser.Object,
            context.Clock.Object,
            new AutoBalanceTeamsCommandValidator(),
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object,
            context.Audits.Object,
            context.UnitOfWork.Object);

    [Fact]
    public async Task CaptainAssignment_WhenKnownValidatorMatches_ReturnsNotModified()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        context.Match.DraftRevision = 12;
        context.ConfigureMatch([context.Team(context.Actor.Id, 1)]);
        var handler = new GetCaptainAssignmentQueryHandler(
            context.CurrentUser.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Profiles.Object,
            context.Stats.Object);

        var current = await handler.HandleAsync(context.Session.Id);
        context.Stats.Invocations.Clear();

        var result = await handler.HandleConditionalAsync(context.Session.Id, current.DraftValidator);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TeamDraft_WhenKnownValidatorMatches_ReturnsNotModified()
    {
        var context = new TestContext(postGame: false, isGameAdmin: true);
        context.Match.DraftRevision = 8;
        context.ConfigureMatch([context.Team(context.Actor.Id, 1)]);
        var handler = new GetTeamDraftQueryHandler(
            context.CurrentUser.Object,
            context.Clock.Object,
            context.Profiles.Object,
            context.Sessions.Object,
            context.Rsvps.Object,
            context.PickupPalGames.Object,
            context.Stats.Object);

        var current = await handler.HandleAsync(context.Session.Id);
        context.Stats.Invocations.Clear();

        var result = await handler.HandleConditionalAsync(context.Session.Id, current.DraftValidator);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionTeams_WhenKnownRevisionMatches_AuthorizesRosterThenSkipsProjection()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        context.Match.DraftRevision = 4;
        context.ConfigureMatch([context.Team(context.Actor.Id, 1)]);
        context.Rsvps
            .Setup(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([context.Roster(context.Actor.Id, "Ada Green")]);

        var handler = CreateSessionTeamsHandler(context);
        var current = await handler.HandleAsync(context.Session.Id);
        context.Stats.Invocations.Clear();

        var result = await handler.HandleConditionalAsync(context.Session.Id, current.DraftValidator);

        result.Should().BeNull();
        context.Rsvps.Verify(x => x.ListGoingRosterAsync(context.Session.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DraftPick_WhenExpectedRevisionIsStale_RejectsBeforeAssignmentReads()
    {
        var context = new TestContext(postGame: false, isGameAdmin: false);
        context.Match.DraftRevision = 3;
        context.ConfigureMatch([context.Team(context.Actor.Id, 1)]);

        var act = () => CreateDraftPickHandler(context)
            .HandleAsync(new DraftPickCommand(context.Session.Id, Guid.NewGuid(), ExpectedDraftRevision: 2));

        await act.Should().ThrowAsync<ApplicationPreconditionFailedException>().WithMessage("*draft changed*");
        context.Stats.Verify(x => x.ListMatchTeamsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class TestContext
    {
        public TestContext(bool postGame, bool isGameAdmin)
        {
            WireSerializableTransaction();
            Clock.SetupGet(x => x.UtcNow).Returns(postGame
                ? Session.StartsAtUtc.AddMinutes(100)
                : Session.StartsAtUtc.AddMinutes(-5));
            CurrentUser.SetupGet(x => x.UserId).Returns(Actor.IdentityUserId);
            CurrentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(isGameAdmin);
            Profiles
                .Setup(x => x.FindByIdentityUserIdAsync(Actor.IdentityUserId!.Value, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Actor);
            Sessions
                .Setup(x => x.GetByIdAsync(Session.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Session);
            UnitOfWork
                .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            Rsvps
                .Setup(x => x.ListGoingRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<RosterMemberRecord>());
            Rsvps
                .Setup(x => x.ListActiveWaitlistRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<RosterMemberRecord>());
            PickupPalGames
                .Setup(x => x.ListParticipantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PickupPalGameParticipant>());
        }

        public PlayerProfile Actor { get; } = new()
        {
            Id = Guid.NewGuid(),
            IdentityUserId = Guid.NewGuid(),
            DisplayName = "Ada Green",
            PreferredPosition = "MID",
        };

        public Session Session { get; } = new()
        {
            Id = Guid.NewGuid(),
            SeasonId = Guid.NewGuid(),
            VenueId = Guid.NewGuid(),
            Title = "Wednesday Pickup",
            Format = "7v7",
            Capacity = 20,
            TeamCount = 2,
            StartsAtUtc = new DateTime(2026, 7, 23, 2, 40, 0, DateTimeKind.Utc),
            CheckInOpensAtUtc = new DateTime(2026, 7, 23, 2, 30, 0, DateTimeKind.Utc),
            CheckInClosesAtUtc = new DateTime(2026, 7, 23, 2, 45, 0, DateTimeKind.Utc),
            RsvpDeadlineUtc = new DateTime(2026, 7, 23, 1, 40, 0, DateTimeKind.Utc),
            Status = SessionStatus.Published,
        };

        public MatchEntity Match { get; } = new()
        {
            Id = Guid.NewGuid(),
            MatchNumber = 1,
            Status = MatchStatus.Draft,
        };

        public Mock<ICurrentUser> CurrentUser { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IPlayerProfileRepository> Profiles { get; } = new();
        public Mock<ISessionRepository> Sessions { get; } = new();
        public Mock<IRsvpRepository> Rsvps { get; } = new();
        public Mock<IPickupPalGameRepository> PickupPalGames { get; } = new();
        public Mock<IStatsRepository> Stats { get; } = new();
        public Mock<IAuditLogRepository> Audits { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        /// <summary>
        /// The wrapped handlers run their read-check-write core through the serializable
        /// transaction; the test double simply invokes the operation so guards still execute.
        /// </summary>
        public void WireSerializableTransaction() =>
            UnitOfWork
                .Setup(x => x.ExecuteInSerializableTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<GameDayMutationModel>>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<GameDayMutationModel>> operation, string _, CancellationToken token) =>
                    operation(token));

        public void ConfigureMatch(IReadOnlyList<MatchTeam> teams, MatchStatus status = MatchStatus.Draft)
        {
            Match.SessionId = Session.Id;
            Match.Status = status;
            Stats
                .Setup(x => x.FindPrimaryMatchBySessionAsync(Session.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Match);
            Stats
                .Setup(x => x.FindMatchAsync(Match.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Match);
            Stats
                .Setup(x => x.ListMatchTeamsAsync(Match.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(teams);
            Stats
                .Setup(x => x.ListAssignmentsAsync(Match.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TeamAssignment>());
        }

        public RosterMemberRecord Roster(Guid id, string name) =>
            new(id, name, "MID", false, null);

        public MatchTeam Team(Guid captainId, int teamNumber) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = Match.Id,
            TeamNumber = teamNumber,
            Name = teamNumber == 1 ? "Team Green" : "Team White",
            CaptainPlayerProfileId = captainId,
        };

        public TeamAssignment Assignment(Guid teamId, Guid playerId) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = Match.Id,
            MatchTeamId = teamId,
            PlayerProfileId = playerId,
        };

        public MatchResult Result(Guid teamId, int wins = 0, int draws = 0, int losses = 0) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = Match.Id,
            MatchTeamId = teamId,
            Wins = wins,
            Draws = draws,
            Losses = losses,
        };

        public PublishPostGameCommandHandler CreatePublishHandler() => new(
            CurrentUser.Object,
            Clock.Object,
            new PublishPostGameCommandValidator(),
            Profiles.Object,
            Sessions.Object,
            Stats.Object,
            Audits.Object,
            UnitOfWork.Object);
    }
}
