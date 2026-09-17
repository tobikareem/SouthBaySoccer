using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Application.Features.Outbox;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Outbox;

public sealed class OutboxMessageHandlerTests
{
    [Fact]
    public async Task SessionSyncHandler_WhenPushSucceeds_PassesTheRecordedAdminAndCompletes()
    {
        var sessionId = Guid.NewGuid();
        var adminProfileId = Guid.NewGuid();
        var syncService = new Mock<ISessionPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(sessionId, adminProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(PickupPalSyncStatus.Synced, null));
        var handler = new SessionPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            SessionOutboxMessages.SessionPickupPalSyncRequested,
            $$"""{"SessionId":"{{sessionId}}","Action":"EnsureCreated","ActingPlayerProfileId":"{{adminProfileId}}"}"""));

        handler.MessageType.Should().Be(SessionOutboxMessages.SessionPickupPalSyncRequested);
        result.Disposition.Should().Be(OutboxHandlingDisposition.Completed);
        syncService.Verify(x => x.PushCurrentStateAsync(sessionId, adminProfileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SessionSyncHandler_WhenNoAdminIsRecorded_PassesNullAdmin()
    {
        var sessionId = Guid.NewGuid();
        var syncService = new Mock<ISessionPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(sessionId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(PickupPalSyncStatus.Failed, SessionPickupPalSyncErrorCodes.MissingCreator));
        var handler = new SessionPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            SessionOutboxMessages.SessionPickupPalSyncRequested,
            $$"""{"SessionId":"{{sessionId}}","Action":"EnsureCreated","ActingPlayerProfileId":null}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Completed, "a terminal failure is recorded on the session, not retried");
    }

    [Fact]
    public async Task SessionSyncHandler_WhenPushIsRetryable_AsksForRetryWithTheCode()
    {
        var syncService = new Mock<ISessionPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, SessionPickupPalSyncErrorCodes.Unavailable));
        var handler = new SessionPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            SessionOutboxMessages.SessionPickupPalSyncRequested,
            $$"""{"SessionId":"{{Guid.NewGuid()}}"}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Retry);
        result.Code.Should().Be(SessionPickupPalSyncErrorCodes.Unavailable);
    }

    [Fact]
    public async Task SessionSyncHandler_WhenPayloadHasNoSessionId_FailsPermanently()
    {
        var handler = new SessionPickupPalSyncOutboxHandler(Mock.Of<ISessionPickupPalSyncService>());

        var result = await handler.HandleAsync(Message(SessionOutboxMessages.SessionPickupPalSyncRequested, "{}"));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Fail);
        result.Code.Should().Be(SessionPickupPalSyncOutboxHandler.InvalidPayloadCode);
    }

    [Fact]
    public async Task RsvpSyncHandler_WhenPushSucceeds_Completes()
    {
        var sessionId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        var syncService = new Mock<IRsvpPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(sessionId, playerProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(PickupPalSyncStatus.Synced, null));
        var handler = new RsvpPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            RsvpOutboxMessages.RsvpPickupPalSyncRequested,
            $$"""{"SessionId":"{{sessionId}}","PlayerProfileId":"{{playerProfileId}}","PickupPalGameId":"game-1","DesiredState":"Add"}"""));

        handler.MessageType.Should().Be(RsvpOutboxMessages.RsvpPickupPalSyncRequested);
        result.Disposition.Should().Be(OutboxHandlingDisposition.Completed);
    }

    [Theory]
    [InlineData(PickupPalSyncStatus.Failed, "GameFull")]
    [InlineData(PickupPalSyncStatus.NotApplicable, null)]
    public async Task RsvpSyncHandler_WhenOutcomeIsTerminal_Completes(PickupPalSyncStatus status, string? code)
    {
        var syncService = new Mock<IRsvpPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(status, code));
        var handler = new RsvpPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            RsvpOutboxMessages.RsvpPickupPalSyncRequested,
            $$"""{"SessionId":"{{Guid.NewGuid()}}","PlayerProfileId":"{{Guid.NewGuid()}}"}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Completed);
    }

    [Fact]
    public async Task RsvpSyncHandler_WhenPushIsRetryable_AsksForRetryWithTheCode()
    {
        var syncService = new Mock<IRsvpPickupPalSyncService>();
        syncService
            .Setup(x => x.PushCurrentStateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, PickupPalSyncErrorCodes.Unavailable));
        var handler = new RsvpPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(
            RsvpOutboxMessages.RsvpPickupPalSyncRequested,
            $$"""{"SessionId":"{{Guid.NewGuid()}}","PlayerProfileId":"{{Guid.NewGuid()}}"}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Retry);
        result.Code.Should().Be(PickupPalSyncErrorCodes.Unavailable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"SessionId":"nope","PlayerProfileId":"nope"}""")]
    public async Task RsvpSyncHandler_WhenPayloadIsUnreadable_FailsWithoutPushing(string payload)
    {
        var syncService = new Mock<IRsvpPickupPalSyncService>(MockBehavior.Strict);
        var handler = new RsvpPickupPalSyncOutboxHandler(syncService.Object);

        var result = await handler.HandleAsync(Message(RsvpOutboxMessages.RsvpPickupPalSyncRequested, payload));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Fail);
        result.Code.Should().Be(RsvpPickupPalSyncOutboxHandler.InvalidPayloadCode);
    }

    [Fact]
    public async Task DeletionHandler_WhenDeleteSucceeds_Completes()
    {
        var onboardingClient = new Mock<IPickupPalOnboardingClient>();
        var handler = new PickupPalUserDeletionOutboxHandler(onboardingClient.Object);

        var result = await handler.HandleAsync(Message(
            OnboardingOutboxMessages.PickupPalUserDeletionRequested,
            """{"IdentityUserId":"7b6b9e2e-7f3c-4d1b-9c4b-0a1f2e3d4c5b","PickupPalUserId":"pp-user-9"}"""));

        handler.MessageType.Should().Be(OnboardingOutboxMessages.PickupPalUserDeletionRequested);
        result.Disposition.Should().Be(OutboxHandlingDisposition.Completed);
        onboardingClient.Verify(x => x.DeleteUserAsync("pp-user-9", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletionHandler_WhenPickupPalUnavailable_AsksForRetry()
    {
        var onboardingClient = new Mock<IPickupPalOnboardingClient>();
        onboardingClient
            .Setup(x => x.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("down"));
        var handler = new PickupPalUserDeletionOutboxHandler(onboardingClient.Object);

        var result = await handler.HandleAsync(Message(
            OnboardingOutboxMessages.PickupPalUserDeletionRequested,
            """{"PickupPalUserId":"pp-user-9"}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Retry);
        result.Code.Should().Be(PickupPalSyncErrorCodes.Unavailable);
    }

    [Fact]
    public async Task DeletionHandler_WhenPayloadLacksUserId_Fails()
    {
        var handler = new PickupPalUserDeletionOutboxHandler(Mock.Of<IPickupPalOnboardingClient>());

        var result = await handler.HandleAsync(Message(
            OnboardingOutboxMessages.PickupPalUserDeletionRequested,
            """{"PickupPalUserId":null}"""));

        result.Disposition.Should().Be(OutboxHandlingDisposition.Fail);
        result.Code.Should().Be(PickupPalUserDeletionOutboxHandler.InvalidPayloadCode);
    }

    private static OutboxMessage Message(string type, string payloadJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            MessageType = type,
            PayloadJson = payloadJson,
            Status = OutboxMessageStatus.Processing,
        };
}
