using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Onboarding;

public sealed class DeleteAccountCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid IdentityUserId = Guid.NewGuid();
    private static readonly Guid PlayerProfileId = Guid.NewGuid();
    private const string PickupPalUserId = "clx8f9a2b0001qwer5678efgh";

    private readonly Mock<ICurrentUser> currentUser = new();
    private readonly Mock<ILocalAccountDeletionService> localDeletion = new();
    private readonly Mock<IOutboxMessageRepository> outbox = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IPickupPalOnboardingClient> onboardingClient = new();
    private readonly List<OutboxMessage> enqueued = [];
    private readonly List<string> calls = [];

    public DeleteAccountCommandHandlerTests()
    {
        currentUser.SetupGet(x => x.UserId).Returns(IdentityUserId);
        localDeletion
            .Setup(x => x.DeleteAsync(IdentityUserId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("local-delete"))
            .ReturnsAsync(new LocalAccountDeletion(PlayerProfileId, PickupPalUserId));
        outbox
            .Setup(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((message, _) =>
            {
                calls.Add("outbox");
                enqueued.Add(message);
            })
            .Returns(Task.CompletedTask);
        onboardingClient
            .Setup(x => x.DeleteUserAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("pickuppal-delete"))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalDeleteSucceeds_DeletesLocallyFirstThenMarksOutboxProcessed()
    {
        await CreateHandler().HandleAsync();

        calls.Should().Equal("local-delete", "outbox", "pickuppal-delete");
        var message = enqueued.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(OnboardingOutboxMessages.PickupPalUserDeletionRequested);
        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(Now);
        message.AttemptCount.Should().Be(1);
        message.PayloadJson.Should().Contain(PickupPalUserId);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WhenPickupPalDeleteFails_KeepsLocalDeletionAndLeavesOutboxForRetry()
    {
        onboardingClient
            .Setup(x => x.DeleteUserAsync(PickupPalUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationServiceUnavailableException("Pickup Pal is unavailable right now. Try again later."));

        var act = () => CreateHandler().HandleAsync();

        await act.Should().NotThrowAsync();
        localDeletion.Verify(x => x.DeleteAsync(IdentityUserId, It.IsAny<CancellationToken>()), Times.Once);
        var message = enqueued.Should().ContainSingle().Subject;
        message.Status.Should().Be(OutboxMessageStatus.RetryScheduled);
        message.ProcessedAtUtc.Should().BeNull();
        message.AvailableAtUtc.Should().BeAfter(Now);
        message.AttemptCount.Should().Be(1);
        outbox.Verify(x => x.Update(message), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_WhenAccountNotLinkedToPickupPal_DeletesLocallyWithoutOutbox()
    {
        localDeletion
            .Setup(x => x.DeleteAsync(IdentityUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalAccountDeletion(PlayerProfileId, null));

        await CreateHandler().HandleAsync();

        enqueued.Should().BeEmpty();
        onboardingClient.Verify(x => x.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAnonymous_ThrowsWithoutDeleting()
    {
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);

        var act = () => CreateHandler().HandleAsync();

        await act.Should().ThrowAsync<ApplicationUnauthenticatedException>();
        localDeletion.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private DeleteAccountCommandHandler CreateHandler()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        return new DeleteAccountCommandHandler(
            currentUser.Object,
            localDeletion.Object,
            outbox.Object,
            unitOfWork.Object,
            clock.Object,
            onboardingClient.Object,
            NullLogger<DeleteAccountCommandHandler>.Instance);
    }
}
