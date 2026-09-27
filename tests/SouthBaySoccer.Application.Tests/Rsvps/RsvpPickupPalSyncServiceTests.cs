using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Rsvps;

public sealed class RsvpPickupPalSyncServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenSessionIsAppOnly_ReturnsNotApplicableWithoutCallingPickupPal()
    {
        var context = new TestContext(occurrenceKey: "weekly:2026-09-16");

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.GamesClient.VerifyNoOtherCalls();
        context.OutboxRepository.Verify(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenSessionCarriesAGameIdButAnotherOccurrenceKey_SyncsByGameId()
    {
        // An app-created recurrence occurrence keeps its recurrence key; the stored game id is the link.
        var context = new TestContext(occurrenceKey: $"{Guid.NewGuid():N}:20260916T180000Z", pickupPalGameId: "game-7");
        context.GamesClient
            .Setup(x => x.AddPlayerAsync("game-7", "pp-user-1", "Ada Lovelace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);
        context.GamesClient
            .Setup(x => x.GetGameAsync("game-7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleGame("game-7"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenProfileHasNoPickupPalUserId_ReturnsNotApplicableWithoutCallingPickupPal()
    {
        var context = new TestContext(pickupPalUserId: null);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.GamesClient.VerifyNoOtherCalls();
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPlayerIsGoing_AddsPlayerRefreshesGameAndMarksSynced()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        var refreshedGame = SampleGame("game-1");
        context.GamesClient
            .Setup(x => x.AddPlayerAsync("game-1", "pp-user-1", "Ada Lovelace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);
        context.GamesClient
            .Setup(x => x.GetGameAsync("game-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshedGame);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.ImportService.Verify(
            x => x.UpsertAsync(
                It.Is<IReadOnlyList<PickupPalGame>>(games => games.Count == 1 && games[0] == refreshedGame),
                It.IsAny<CancellationToken>()),
            Times.Once);
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        context.Rsvp.PickupPalSyncedAtUtc.Should().Be(NowUtc);
        context.Rsvp.PickupPalSyncError.Should().BeNull();
        var message = context.AddedOutboxMessages.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(RsvpOutboxMessages.RsvpPickupPalSyncRequested);
        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(NowUtc);
        message.IdempotencyKey.Should().Be(
            $"RsvpPickupPalSyncRequested:{context.Session.Id:D}:{context.Profile.Id:D}:Add");
        message.PayloadJson.Should().Contain("game-1").And.NotContain("Ada").And.NotContain("pp-user-1");
        // Write-before-call, then the settled outcome: two commits.
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPlayerIsWaitlistedLocally_StillPushesAdd()
    {
        var context = new TestContext(localState: RsvpMutationState.Waitlisted);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync("game-1", "pp-user-1", "Ada Lovelace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.GamesClient.Verify(
            x => x.RemovePlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(RsvpMutationState.NotGoing)]
    [InlineData(RsvpMutationState.Maybe)]
    [InlineData(null)]
    public async Task SyncAfterLocalWriteAsync_WhenPlayerIsNotGoingMaybeOrCanceled_RemovesPlayer(RsvpMutationState? localState)
    {
        var context = new TestContext(localState: localState);
        context.GamesClient
            .Setup(x => x.RemovePlayerAsync("game-1", "pp-user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.GamesClient.Verify(
            x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.AddedOutboxMessages.Single().IdempotencyKey.Should().EndWith(":Remove");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPickupPalIsUnavailable_LeavesPendingAndSchedulesOutboxRetry()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Pending);
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Pending);
        context.Rsvp.PickupPalSyncError.Should().Be(PickupPalSyncErrorCodes.Unavailable);
        var message = context.AddedOutboxMessages.Single();
        message.Status.Should().Be(OutboxMessageStatus.RetryScheduled);
        message.AvailableAtUtc.Should().Be(NowUtc.AddMinutes(1));
        message.AttemptCount.Should().Be(1);
        context.GamesClient.Verify(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenGameIsFull_MarksFailedGameFullAndStillRefreshesSnapshot()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.GameFull);
        context.GamesClient
            .Setup(x => x.GetGameAsync("game-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleGame("game-1"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Failed);
        context.Rsvp.PickupPalSyncError.Should().Be(PickupPalSyncErrorCodes.GameFull);
        context.ImportService.Verify(
            x => x.UpsertAsync(It.IsAny<IReadOnlyList<PickupPalGame>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Terminal: no retry until the player changes their RSVP again.
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenGameIsGone_MarksFailedGameNotFoundWithoutRefresh()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.GameNotFound);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Rsvp.PickupPalSyncError.Should().Be(PickupPalSyncErrorCodes.GameNotFound);
        context.GamesClient.Verify(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPlayerAlreadyOnRoster_MarksSynced()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.AlreadyApplied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenRefreshFailsAfterSuccessfulPush_LeavesPendingForRetry()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);
        context.GamesClient
            .Setup(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Pending);
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.RetryScheduled);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenDeadLetteredRowExists_ReopensItForTheNewRequest()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        var existing = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = RsvpOutboxMessages.RsvpPickupPalSyncRequested,
            Status = OutboxMessageStatus.DeadLettered,
            AttemptCount = 6,
            DeadLetterReason = "MaxAttemptsExceeded:Unavailable",
            IdempotencyKey = RsvpPickupPalSyncService.BuildIdempotencyKey(context.Session.Id, context.Profile.Id, PickupPalRosterDesiredState.Add),
        };
        context.OutboxRepository
            .Setup(x => x.FindByIdempotencyKeyAsync(existing.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        context.AddedOutboxMessages.Should().BeEmpty();
        existing.Status.Should().Be(OutboxMessageStatus.Processed);
        existing.AttemptCount.Should().Be(0);
        existing.DeadLetterReason.Should().BeNull();
        context.OutboxRepository.Verify(x => x.Update(existing), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenProcessorHoldsTheRow_PushesButLeavesTheRowToItsOwner()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        var locked = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = RsvpOutboxMessages.RsvpPickupPalSyncRequested,
            Status = OutboxMessageStatus.Processing,
            LockToken = "other-run",
            LockedUntilUtc = NowUtc.AddMinutes(4),
            IdempotencyKey = RsvpPickupPalSyncService.BuildIdempotencyKey(context.Session.Id, context.Profile.Id, PickupPalRosterDesiredState.Add),
        };
        context.OutboxRepository
            .Setup(x => x.FindByIdempotencyKeyAsync(locked.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locked);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        locked.Status.Should().Be(OutboxMessageStatus.Processing);
        locked.LockToken.Should().Be("other-run");
        context.OutboxRepository.Verify(x => x.Update(It.IsAny<OutboxMessage>()), Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPersistenceThrows_DiscardsTrackedStateAndReportsFailedInsteadOfFailingTheRsvp()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection dropped"));

        var act = () => context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        (await act.Should().NotThrowAsync()).Which.Should().Be(PickupPalSyncStatus.Failed);
        context.UnitOfWork.Verify(x => x.DiscardChanges(), Times.AtLeastOnce);
        // Best-effort attempt to record the failure on the RSVP row (also fails here, and is swallowed).
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Failed);
        context.Rsvp.PickupPalSyncError.Should().Be(PickupPalSyncErrorCodes.Unexpected);
        context.GamesClient.Verify(
            x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the write-before-call rule means no push happens when the pending record cannot be committed");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenTheProcessorTakesTheRowBeforeTheFirstSave_PushesAndLeavesTheRowToItsOwner()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        var saves = 0;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 1
                ? Task.FromException<int>(new ApplicationConflictException("row version"))
                : Task.FromResult(1));
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
        // The row is not settled by this writer: it stays exactly as the outbox repository handed it out.
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Pending);
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        saves.Should().Be(3, "conflict, re-tracked pending save, settled save");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenTheSettleSaveConflicts_RetracksOnlyTheRsvpRow()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        var saves = 0;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 2
                ? Task.FromException<int>(new ApplicationConflictException("row version"))
                : Task.FromResult(1));
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.UnitOfWork.Verify(x => x.DiscardChanges(), Times.Once);
        context.RsvpRepository.Verify(
            x => x.FindRsvpForPickupPalSyncAsync(context.Session.Id, context.Profile.Id, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        saves.Should().Be(3);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenCallerCancels_PropagatesCancellation()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        using var cts = new CancellationTokenSource();
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, string, CancellationToken>((_, _, _, _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => context.Service.SyncAfterLocalWriteAsync(context.Session.Id, context.Profile.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PushCurrentStateAsync_WhenPlayerIsGoing_PushesAndUpdatesRsvpWithoutTouchingOutboxOrSaving()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync("game-1", "pp-user-1", "Ada Lovelace", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalRosterPushResult.Applied);

        var outcome = await context.Service.PushCurrentStateAsync(context.Session.Id, context.Profile.Id);

        outcome.Status.Should().Be(PickupPalSyncStatus.Synced);
        outcome.IsRetryable.Should().BeFalse();
        context.Rsvp.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        context.OutboxRepository.VerifyNoOtherCalls();
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PushCurrentStateAsync_WhenUnavailable_ReportsRetryableOutcome()
    {
        var context = new TestContext(localState: RsvpMutationState.Going);
        context.GamesClient
            .Setup(x => x.AddPlayerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));

        var outcome = await context.Service.PushCurrentStateAsync(context.Session.Id, context.Profile.Id);

        outcome.IsRetryable.Should().BeTrue();
        outcome.ErrorCode.Should().Be(PickupPalSyncErrorCodes.Unavailable);
    }

    [Fact]
    public async Task PushCurrentStateAsync_WhenSessionIsAppOnly_ReturnsNotApplicable()
    {
        var context = new TestContext(occurrenceKey: null);

        var outcome = await context.Service.PushCurrentStateAsync(context.Session.Id, context.Profile.Id);

        outcome.Status.Should().Be(PickupPalSyncStatus.NotApplicable);
    }

    [Fact]
    public async Task Gate_WhenTwoPushesRaceForOnePlayer_SerializesThem()
    {
        var gate = new RsvpPickupPalSyncGate();
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var first = await gate.AcquireAsync(sessionId, playerId);
        var second = gate.AcquireAsync(sessionId, playerId);
        var other = await gate.AcquireAsync(sessionId, Guid.NewGuid());

        second.IsCompleted.Should().BeFalse("the same player and session must wait for the first lease");
        other.Dispose();
        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    private static PickupPalGame SampleGame(string id) =>
        new(id, NowUtc.AddDays(1), "Field", 10, "active", "Fire FC", []);

    private sealed class TestContext
    {
        public Session Session { get; }

        public PlayerProfile Profile { get; }

        public RsvpResponse Rsvp { get; }

        public Mock<IPickupPalGamesClient> GamesClient { get; } = new(MockBehavior.Strict);

        public Mock<IPickupPalGameImportService> ImportService { get; } = new();

        public Mock<IOutboxMessageRepository> OutboxRepository { get; } = new();

        public Mock<IRsvpRepository> RsvpRepository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public List<OutboxMessage> AddedOutboxMessages { get; } = [];

        public RsvpPickupPalSyncService Service { get; }

        public TestContext(
            string? occurrenceKey = "pickuppal:game-1",
            string? pickupPalUserId = "pp-user-1",
            RsvpMutationState? localState = RsvpMutationState.Going,
            string? pickupPalGameId = null)
        {
            Session = new Session { Id = Guid.NewGuid(), OccurrenceKey = occurrenceKey, PickupPalGameId = pickupPalGameId, Status = SessionStatus.Published };
            Profile = new PlayerProfile { Id = Guid.NewGuid(), DisplayName = "Ada Lovelace", PickupPalUserId = pickupPalUserId };
            Rsvp = new RsvpResponse { Id = Guid.NewGuid(), SessionId = Session.Id, PlayerProfileId = Profile.Id, Status = RsvpStatus.Going };

            var sessionRepository = new Mock<ISessionRepository>();
            sessionRepository.Setup(x => x.GetByIdAsync(Session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Session);
            var profileRepository = new Mock<IPlayerProfileRepository>();
            profileRepository.Setup(x => x.FindProfileAsync(Profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            RsvpRepository
                .Setup(x => x.GetMyRsvpAsync(Session.Id, Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(localState is { } state
                    ? new RsvpMutationResult(Session.Id, Profile.Id, state, Rsvp.Id)
                    : null);
            RsvpRepository
                .Setup(x => x.FindRsvpForPickupPalSyncAsync(Session.Id, Profile.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Rsvp);
            OutboxRepository
                .Setup(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
                .Callback<OutboxMessage, CancellationToken>((message, _) => AddedOutboxMessages.Add(message))
                .Returns(Task.CompletedTask);
            OutboxRepository
                .Setup(x => x.FindByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((OutboxMessage?)null);
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            ImportService
                .Setup(x => x.UpsertAsync(It.IsAny<IReadOnlyList<PickupPalGame>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PickupPalImportResult(1, 0, []));
            // Refreshing after a push is the default happy path; individual tests override it.
            GamesClient
                .Setup(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SampleGame("game-1"));
            var clock = new Mock<IClock>();
            clock.SetupGet(x => x.UtcNow).Returns(NowUtc);

            Service = new RsvpPickupPalSyncService(
                sessionRepository.Object,
                profileRepository.Object,
                RsvpRepository.Object,
                GamesClient.Object,
                ImportService.Object,
                OutboxRepository.Object,
                UnitOfWork.Object,
                new RsvpPickupPalSyncGate(),
                clock.Object,
                NullLogger<RsvpPickupPalSyncService>.Instance);
        }
    }
}
