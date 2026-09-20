using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class SessionPickupPalSyncServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartUtc = new(2026, 9, 19, 2, 40, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPublishedWithGroupAndNoGame_CreatesGameStoresIdAndOccurrenceKey()
    {
        var context = new TestContext();
        PickupPalGameCreateRequest? observed = null;
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PickupPalGameCreateRequest, CancellationToken>((request, _) => observed = request)
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));
        var refreshed = SampleGame("game-9");
        context.GamesClient
            .Setup(x => x.GetGameAsync("game-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshed);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        observed.Should().NotBeNull();
        observed!.GroupId.Should().Be("1408-1520@g.us");
        observed.CreatorId.Should().Be("pp-admin-1");
        observed.StartsAtUtc.Should().Be(StartUtc);
        observed.TimeZoneId.Should().Be("America/Los_Angeles");
        observed.Location.Should().Be("Caribbean Park, 969 E Caribbean Dr, Sunnyvale");
        observed.MaxPlayers.Should().Be(14);
        context.Session.PickupPalGameId.Should().Be("game-9");
        context.Session.PickupPalOrigin.Should().Be(PickupPalOrigin.CreatedByApp);
        context.Session.OccurrenceKey.Should().Be("pickuppal:game-9");
        context.Session.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        context.Session.PickupPalSyncedAtUtc.Should().Be(NowUtc);
        context.Session.PickupPalSyncError.Should().BeNull();
        context.ImportService.Verify(
            x => x.UpsertAsync(
                It.Is<IReadOnlyList<PickupPalGame>>(games => games.Count == 1 && games[0] == refreshed),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var message = context.AddedOutboxMessages.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(SessionOutboxMessages.SessionPickupPalSyncRequested);
        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.IdempotencyKey.Should().Be($"SessionPickupPalSyncRequested:{context.Session.Id:D}:EnsureCreated");
        message.PayloadJson.Should().Contain(context.Profile.Id.ToString()).And.NotContain("pp-admin-1").And.NotContain("g.us");
        // Write-before-call, the game id right after the create, then the settled outcome.
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenSessionIsARecurrenceOccurrence_KeepsItsOccurrenceKeyAndLinksByGameId()
    {
        var recurrenceKey = $"{Guid.NewGuid():N}:20260919T024000Z";
        var context = new TestContext(occurrenceKey: recurrenceKey);
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.Session.PickupPalGameId.Should().Be("game-9");
        context.Session.OccurrenceKey.Should().Be(recurrenceKey, "the recurrence key is what occurrence creation dedupes on");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenStoringTheGameIdLosesARowVersionRace_AppliesEverythingToTheReloadedInstance()
    {
        var context = new TestContext();
        var stale = context.Session;
        var fresh = context.CloneSession();
        var saves = 0;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 2
                ? throw new ApplicationConflictException("row version moved")
                : Task.FromResult(1));
        context.SessionRepository
            .SetupSequence(x => x.FindForPickupPalSyncAsync(stale.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale)
            .ReturnsAsync(fresh);
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));

        var status = await context.Service.SyncAfterLocalWriteAsync(stale.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        fresh.PickupPalGameId.Should().Be("game-9");
        fresh.PickupPalOrigin.Should().Be(PickupPalOrigin.CreatedByApp);
        fresh.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Synced);
        stale.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Pending, "no later write may land on the detached instance");
        context.GamesClient.Verify(
            x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenTheFirstSaveLosesARowVersionRace_ReopensTheRowAgainAndLeavesItPending()
    {
        var context = new TestContext();
        var fresh = context.CloneSession();
        var saves = 0;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 1
                ? throw new ApplicationConflictException("row version moved")
                : Task.FromResult(1));
        context.SessionRepository
            .SetupSequence(x => x.FindForPickupPalSyncAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context.Session)
            .ReturnsAsync(fresh);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Pending);
        fresh.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Pending);
        // The row is opened against the discarded context, then again against the fresh one, so
        // the latest local write always has a retry row.
        context.OutboxRepository.Verify(x => x.FindByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        context.AddedOutboxMessages.Should().HaveCount(2);
        context.GamesClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenGroupHasNoTimezone_DefaultsToLosAngelesAndUsesVenueNameWithoutAddress()
    {
        var context = new TestContext(groupTimezone: null, venueAddress: null);
        PickupPalGameCreateRequest? observed = null;
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PickupPalGameCreateRequest, CancellationToken>((request, _) => observed = request)
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));

        await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        observed!.TimeZoneId.Should().Be(SessionAdminTimeZone.DefaultTimeZoneId);
        observed.Location.Should().Be("Caribbean Park");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPublishedWithoutGroup_IsAppOnlyAndTouchesNothing()
    {
        var context = new TestContext(hasGroup: false);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.GamesClient.VerifyNoOtherCalls();
        context.AddedOutboxMessages.Should().BeEmpty();
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Session.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.NotApplicable);
    }

    [Theory]
    [InlineData(SessionStatus.Draft)]
    [InlineData(SessionStatus.Completed)]
    public async Task SyncAfterLocalWriteAsync_WhenNotPublished_NeverPushes(SessionStatus status)
    {
        var context = new TestContext(status: status);

        var result = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        result.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.GamesClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPublishedWithAppCreatedGame_UpdatesLocationAndCapacityOnly()
    {
        var context = new TestContext(gameId: "game-9", origin: PickupPalOrigin.CreatedByApp, snapshotStartUtc: StartUtc);
        PickupPalGameUpdateRequest? observed = null;
        context.GamesClient
            .Setup(x => x.UpdateGameAsync("game-9", It.IsAny<PickupPalGameUpdateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, PickupPalGameUpdateRequest, CancellationToken>((_, request, _) => observed = request)
            .ReturnsAsync(PickupPalGameWriteResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        observed.Should().NotBeNull();
        observed!.Location.Should().Be("Caribbean Park, 969 E Caribbean Dr, Sunnyvale");
        observed.MaxPlayers.Should().Be(14);
        observed.StartsAtUtc.Should().BeNull("the start matches what Pickup Pal last reported");
        context.GamesClient.Verify(
            x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.AddedOutboxMessages.Single().IdempotencyKey.Should().EndWith(":EnsureUpdated");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenStartChangedSinceLastSnapshot_UpdateCarriesDateTimeAndZone()
    {
        var context = new TestContext(
            gameId: "game-9",
            origin: PickupPalOrigin.CreatedByApp,
            snapshotStartUtc: StartUtc.AddHours(-1));
        PickupPalGameUpdateRequest? observed = null;
        context.GamesClient
            .Setup(x => x.UpdateGameAsync("game-9", It.IsAny<PickupPalGameUpdateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, PickupPalGameUpdateRequest, CancellationToken>((_, request, _) => observed = request)
            .ReturnsAsync(PickupPalGameWriteResult.Applied);

        await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        observed!.StartsAtUtc.Should().Be(StartUtc);
        observed.TimeZoneId.Should().Be("America/Los_Angeles");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPickupPalNoLongerHasTheGameOnUpdate_DropsTheLinkSoTheNextWriteRecreates()
    {
        var context = new TestContext(gameId: "game-9", origin: PickupPalOrigin.CreatedByApp, occurrenceKey: "pickuppal:game-9");
        context.GamesClient
            .Setup(x => x.UpdateGameAsync("game-9", It.IsAny<PickupPalGameUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalGameWriteResult.GameNotFound);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Session.PickupPalSyncError.Should().Be(SessionPickupPalSyncErrorCodes.GameNotFound);
        context.Session.PickupPalGameId.Should().BeNull();
        context.Session.OccurrenceKey.Should().BeNull("the mirrored key goes with the game id");
        context.Session.PickupPalOrigin.Should().Be(PickupPalOrigin.CreatedByApp);
        SessionPickupPalSyncService.ResolveAction(context.Session).Should().Be(SessionPickupPalSyncAction.EnsureCreated);
    }

    [Theory]
    [InlineData(PickupPalGameWriteResult.Applied)]
    [InlineData(PickupPalGameWriteResult.GameNotFound)]
    public async Task SyncAfterLocalWriteAsync_WhenCanceledWithAppCreatedGame_TerminatesAndTreatsGoneAsDone(PickupPalGameWriteResult result)
    {
        var context = new TestContext(status: SessionStatus.Canceled, gameId: "game-9", origin: PickupPalOrigin.CreatedByApp);
        context.GamesClient
            .Setup(x => x.TerminateGameAsync("game-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.AddedOutboxMessages.Single().IdempotencyKey.Should().EndWith(":EnsureTerminated");
        context.GamesClient.Verify(x => x.GetGameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenDeletedWithAppCreatedGame_Terminates()
    {
        var context = new TestContext(gameId: "game-9", origin: PickupPalOrigin.CreatedByApp, isDeleted: true);
        context.GamesClient
            .Setup(x => x.TerminateGameAsync("game-9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalGameWriteResult.Applied);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
    }

    [Theory]
    [InlineData(SessionStatus.Published)]
    [InlineData(SessionStatus.Canceled)]
    public async Task SyncAfterLocalWriteAsync_WhenSessionWasImported_NeverCreatesUpdatesOrTerminates(SessionStatus status)
    {
        var context = new TestContext(status: status, gameId: "game-9", origin: PickupPalOrigin.Imported);

        var result = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        result.Should().Be(PickupPalSyncStatus.NotApplicable);
        context.GamesClient.VerifyNoOtherCalls();
        context.AddedOutboxMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenPickupPalIsUnavailable_LeavesPendingAndSchedulesOutboxRetry()
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Pending);
        context.Session.PickupPalSyncStatus.Should().Be(PickupPalSyncStatus.Pending);
        context.Session.PickupPalSyncError.Should().Be(SessionPickupPalSyncErrorCodes.Unavailable);
        context.Session.PickupPalGameId.Should().BeNull();
        var message = context.AddedOutboxMessages.Single();
        message.Status.Should().Be(OutboxMessageStatus.RetryScheduled);
        message.AttemptCount.Should().Be(1);
        message.AvailableAtUtc.Should().Be(NowUtc.AddMinutes(1));
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenAdminHasNoPickupPalUserId_FailsTerminallyWithoutRetry()
    {
        var context = new TestContext(adminPickupPalUserId: null);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Session.PickupPalSyncError.Should().Be(SessionPickupPalSyncErrorCodes.MissingCreator);
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Processed);
        context.GamesClient.Verify(
            x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenGroupHasNoExternalId_FailsTerminallyWithoutRetry()
    {
        var context = new TestContext(groupExternalId: "");

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Session.PickupPalSyncError.Should().Be(SessionPickupPalSyncErrorCodes.MissingGroup);
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Theory]
    [InlineData(PickupPalGameWriteResult.Rejected, "Rejected")]
    [InlineData(PickupPalGameWriteResult.InvalidResponse, "InvalidResponse")]
    public async Task SyncAfterLocalWriteAsync_WhenCreateIsRejected_FailsTerminallyWithSafeCode(PickupPalGameWriteResult result, string code)
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalGameCreateResult(result, null));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.Session.PickupPalSyncError.Should().Be(code);
        context.Session.PickupPalGameId.Should().BeNull();
        context.AddedOutboxMessages.Single().Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenRefreshFails_StillReportsSyncedBecauseTheGameExists()
    {
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));
        context.GamesClient
            .Setup(x => x.GetGameAsync("game-9", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Synced);
        context.Session.PickupPalGameId.Should().Be("game-9");
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenProcessorHoldsTheRow_ReopensItAndDoesNotPush()
    {
        var context = new TestContext();
        var held = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = SessionOutboxMessages.SessionPickupPalSyncRequested,
            Status = OutboxMessageStatus.Processing,
            LockToken = "other",
            LockedUntilUtc = NowUtc.AddMinutes(4),
            AttemptCount = 2,
            IdempotencyKey = SessionPickupPalSyncService.BuildIdempotencyKey(context.Session.Id, SessionPickupPalSyncAction.EnsureCreated),
        };
        context.OutboxRepository
            .Setup(x => x.FindByIdempotencyKeyAsync(held.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(held);

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Pending);
        held.Status.Should().Be(OutboxMessageStatus.Pending);
        held.LockToken.Should().BeNull();
        held.AttemptCount.Should().Be(0);
        context.GamesClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SyncAfterLocalWriteAsync_WhenBookkeepingCannotBeSaved_ReportsFailedAndDiscardsTrackedState()
    {
        var context = new TestContext();
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var status = await context.Service.SyncAfterLocalWriteAsync(context.Session.Id);

        status.Should().Be(PickupPalSyncStatus.Failed);
        context.UnitOfWork.Verify(x => x.DiscardChanges(), Times.AtLeastOnce);
        context.GamesClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PushCurrentStateAsync_WhenRetriedFromOutbox_UsesTheRecordedAdminAsCreatorAndDoesNotSettleRows()
    {
        var context = new TestContext(currentUserSignedIn: false);
        context.GamesClient
            .Setup(x => x.CreateGameAsync(It.Is<PickupPalGameCreateRequest>(r => r.CreatorId == "pp-admin-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalGameCreateResult(PickupPalGameWriteResult.Applied, "game-9"));

        var outcome = await context.Service.PushCurrentStateAsync(context.Session.Id, context.Profile.Id);

        outcome.Status.Should().Be(PickupPalSyncStatus.Synced);
        context.Session.PickupPalGameId.Should().Be("game-9");
        context.AddedOutboxMessages.Should().BeEmpty();
        // Only the created game id is persisted eagerly; the processor commits the rest.
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PushCurrentStateAsync_TakesTheSessionLeaseBeforeReadingTheSession()
    {
        // A processor claim that overlaps an in-flight create must wait for the lease and then
        // read the row that already carries the game id; reading first would re-derive
        // EnsureCreated from a stale row and POST a second game.
        var context = new TestContext();
        context.GamesClient
            .Setup(x => x.UpdateGameAsync("game-9", It.IsAny<PickupPalGameUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PickupPalGameWriteResult.Applied);
        var readCount = 0;
        context.SessionRepository
            .Setup(x => x.FindForPickupPalSyncAsync(context.Session.Id, It.IsAny<CancellationToken>()))
            .Callback(() => readCount++)
            .ReturnsAsync(() => context.Session);
        var lease = await context.Gate.AcquireAsync(context.Session.Id);

        var push = context.Service.PushCurrentStateAsync(context.Session.Id, context.Profile.Id);
        await Task.Delay(50);
        readCount.Should().Be(0, "the session is read only once the lease is held");
        // The create that held the lease finishes and stores the game id before releasing.
        context.Session.PickupPalGameId = "game-9";
        context.Session.PickupPalOrigin = PickupPalOrigin.CreatedByApp;
        lease.Dispose();
        var outcome = await push;

        readCount.Should().Be(1);
        outcome.Status.Should().Be(PickupPalSyncStatus.Synced);
        context.GamesClient.Verify(
            x => x.CreateGameAsync(It.IsAny<PickupPalGameCreateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PushCurrentStateAsync_WhenNoAdminIsRecorded_FailsWithMissingCreator()
    {
        var context = new TestContext(currentUserSignedIn: false);

        var outcome = await context.Service.PushCurrentStateAsync(context.Session.Id, actingPlayerProfileId: null);

        outcome.Status.Should().Be(PickupPalSyncStatus.Failed);
        outcome.ErrorCode.Should().Be(SessionPickupPalSyncErrorCodes.MissingCreator);
        outcome.IsRetryable.Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionStatus.Published, PickupPalOrigin.None, null, true, false, SessionPickupPalSyncAction.EnsureCreated)]
    [InlineData(SessionStatus.Published, PickupPalOrigin.None, null, false, false, SessionPickupPalSyncAction.None)]
    [InlineData(SessionStatus.Draft, PickupPalOrigin.None, null, true, false, SessionPickupPalSyncAction.None)]
    [InlineData(SessionStatus.Published, PickupPalOrigin.CreatedByApp, "g", true, false, SessionPickupPalSyncAction.EnsureUpdated)]
    [InlineData(SessionStatus.Canceled, PickupPalOrigin.CreatedByApp, "g", true, false, SessionPickupPalSyncAction.EnsureTerminated)]
    [InlineData(SessionStatus.Published, PickupPalOrigin.CreatedByApp, "g", true, true, SessionPickupPalSyncAction.EnsureTerminated)]
    [InlineData(SessionStatus.Canceled, PickupPalOrigin.None, null, true, false, SessionPickupPalSyncAction.None)]
    [InlineData(SessionStatus.Published, PickupPalOrigin.Imported, "g", true, false, SessionPickupPalSyncAction.None)]
    [InlineData(SessionStatus.Canceled, PickupPalOrigin.Imported, "g", true, false, SessionPickupPalSyncAction.None)]
    public void ResolveAction_GivenLocalState_DerivesTheAction(
        SessionStatus status,
        PickupPalOrigin origin,
        string? gameId,
        bool hasGroup,
        bool isDeleted,
        SessionPickupPalSyncAction expected)
    {
        var session = new Session
        {
            Status = status,
            PickupPalOrigin = origin,
            PickupPalGameId = gameId,
            GroupChatId = hasGroup ? Guid.NewGuid() : null,
            IsDeleted = isDeleted,
        };

        SessionPickupPalSyncService.ResolveAction(session).Should().Be(expected);
    }

    private static PickupPalGame SampleGame(string gameId) =>
        new(gameId, StartUtc, "Caribbean Park, 969 E Caribbean Dr, Sunnyvale", 14, "active", "South Bay", []);

    private sealed class TestContext
    {
        public Session Session { get; }

        public PlayerProfile Profile { get; }

        public Mock<IPickupPalGamesClient> GamesClient { get; } = new(MockBehavior.Strict);

        public Mock<ISessionRepository> SessionRepository { get; } = new();

        public SessionPickupPalSyncGate Gate { get; } = new();

        public Mock<IPickupPalGameImportService> ImportService { get; } = new();

        public Mock<IOutboxMessageRepository> OutboxRepository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public List<OutboxMessage> AddedOutboxMessages { get; } = [];

        public SessionPickupPalSyncService Service { get; }

        public TestContext(
            SessionStatus status = SessionStatus.Published,
            bool hasGroup = true,
            string? gameId = null,
            PickupPalOrigin origin = PickupPalOrigin.None,
            bool isDeleted = false,
            string? adminPickupPalUserId = "pp-admin-1",
            string groupExternalId = "1408-1520@g.us",
            string? groupTimezone = "America/Los_Angeles",
            string? venueAddress = "969 E Caribbean Dr, Sunnyvale",
            DateTime? snapshotStartUtc = null,
            bool currentUserSignedIn = true,
            string? occurrenceKey = null)
        {
            var group = new GroupChat
            {
                Id = Guid.NewGuid(),
                ExternalId = groupExternalId,
                GroupName = "South Bay",
                Timezone = groupTimezone,
            };
            var venue = new Venue { Id = Guid.NewGuid(), Name = "Caribbean Park", Locality = "Sunnyvale", Address = venueAddress };
            Session = new Session
            {
                Id = Guid.NewGuid(),
                VenueId = venue.Id,
                Title = "Caribbean Park - Friday pickup",
                Capacity = 14,
                StartsAtUtc = StartUtc,
                Status = status,
                GroupChatId = hasGroup ? group.Id : null,
                PickupPalGameId = gameId,
                PickupPalOrigin = origin,
                IsDeleted = isDeleted,
                OccurrenceKey = occurrenceKey,
            };

            var identityUserId = Guid.NewGuid();
            Profile = new PlayerProfile
            {
                Id = Guid.NewGuid(),
                IdentityUserId = identityUserId,
                DisplayName = "Admin",
                PickupPalUserId = adminPickupPalUserId,
            };

            SessionRepository
                .Setup(x => x.FindForPickupPalSyncAsync(Session.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Session);
            var groupRepository = new Mock<IGroupChatRepository>();
            groupRepository.Setup(x => x.GetByIdAsync(group.Id, It.IsAny<CancellationToken>())).ReturnsAsync(group);
            var venueRepository = new Mock<IVenueRepository>();
            venueRepository.Setup(x => x.GetByIdAsync(venue.Id, It.IsAny<CancellationToken>())).ReturnsAsync(venue);
            var profileRepository = new Mock<IPlayerProfileRepository>();
            profileRepository.Setup(x => x.FindProfileAsync(Profile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            profileRepository.Setup(x => x.FindByIdentityUserIdAsync(identityUserId, It.IsAny<CancellationToken>())).ReturnsAsync(Profile);
            var gameRepository = new Mock<IPickupPalGameRepository>();
            gameRepository
                .Setup(x => x.ListSnapshotsByGameIdsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(snapshotStartUtc is { } snapshotStart && gameId is not null
                    ? [new PickupPalGameSnapshot { Id = Guid.NewGuid(), PickupPalGameId = gameId, SessionId = Session.Id, StartsAtUtc = snapshotStart }]
                    : Array.Empty<PickupPalGameSnapshot>());
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
                .ReturnsAsync((string id, CancellationToken _) => SampleGame(id));
            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(currentUserSignedIn ? identityUserId : null);
            var clock = new Mock<IClock>();
            clock.SetupGet(x => x.UtcNow).Returns(NowUtc);

            Service = new SessionPickupPalSyncService(
                SessionRepository.Object,
                groupRepository.Object,
                venueRepository.Object,
                profileRepository.Object,
                gameRepository.Object,
                GamesClient.Object,
                ImportService.Object,
                OutboxRepository.Object,
                UnitOfWork.Object,
                Gate,
                currentUser.Object,
                clock.Object,
                NullLogger<SessionPickupPalSyncService>.Instance);
        }

        /// <summary>A second, detached instance of the same row, as a fresh query after a conflict returns.</summary>
        public Session CloneSession() => new()
        {
            Id = Session.Id,
            VenueId = Session.VenueId,
            Title = Session.Title,
            Capacity = Session.Capacity,
            StartsAtUtc = Session.StartsAtUtc,
            Status = Session.Status,
            GroupChatId = Session.GroupChatId,
            PickupPalGameId = Session.PickupPalGameId,
            PickupPalOrigin = Session.PickupPalOrigin,
            IsDeleted = Session.IsDeleted,
            OccurrenceKey = Session.OccurrenceKey,
        };
    }
}
