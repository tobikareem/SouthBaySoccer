using FluentAssertions;
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
    private readonly List<OutboxMessage> enqueued = [];
    private readonly List<string> calls = [];

    public DeleteAccountCommandHandlerTests()
    {
        currentUser.SetupGet(x => x.UserId).Returns(IdentityUserId);
        SetupLocalDeletion(new LocalAccountDeletion(PlayerProfileId, PickupPalUserId));
        outbox
            .Setup(x => x.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((message, _) =>
            {
                calls.Add("audit");
                enqueued.Add(message);
            })
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_WhenLinkedToPickupPal_DeletesLocallyAndWritesAuditInsideTheTransaction()
    {
        await CreateHandler().HandleAsync();

        calls.Should().Equal("local-delete", "audit", "local-commit");
        var message = enqueued.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(OnboardingOutboxMessages.N9jaBayAccountDeleted);
        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(Now);
        message.IdempotencyKey.Should().Be($"{OnboardingOutboxMessages.N9jaBayAccountDeleted}:{IdentityUserId:D}");
        message.PayloadJson.Should().Contain("\"PickupPalAccountRetained\":true");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenLinkedToPickupPal_NeverQueuesAPickupPalDeletion()
    {
        await CreateHandler().HandleAsync();

        enqueued.Should().NotContain(m => m.MessageType == OnboardingOutboxMessages.PickupPalUserDeletionRequested);
        enqueued.Should().OnlyContain(m => m.Status == OutboxMessageStatus.Processed,
            "a pending row would be picked up by the outbox processor and sent to Pickup Pal");
    }

    [Fact]
    public async Task HandleAsync_WhenAccountNotLinkedToPickupPal_DeletesLocallyAndWritesAudit()
    {
        SetupLocalDeletion(new LocalAccountDeletion(PlayerProfileId, null));

        await CreateHandler().HandleAsync();

        enqueued.Should().ContainSingle()
            .Which.MessageType.Should().Be(OnboardingOutboxMessages.N9jaBayAccountDeleted);
    }

    [Fact]
    public async Task HandleAsync_WhenAuditRowAlreadyExists_DoesNotInsertItAgain()
    {
        // Double tap, retried request, or execution-strategy replay: the unique idempotency key
        // would make a second insert fail, so the existing audit row is kept.
        var existing = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = OnboardingOutboxMessages.N9jaBayAccountDeleted,
            Status = OutboxMessageStatus.Processed,
            IdempotencyKey = $"{OnboardingOutboxMessages.N9jaBayAccountDeleted}:{IdentityUserId:D}",
        };
        outbox
            .Setup(x => x.FindByIdempotencyKeyAsync(existing.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await CreateHandler().HandleAsync();

        enqueued.Should().BeEmpty();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAnonymous_ThrowsWithoutDeleting()
    {
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);

        var act = () => CreateHandler().HandleAsync();

        await act.Should().ThrowAsync<ApplicationUnauthenticatedException>();
        localDeletion.Verify(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Func<LocalAccountDeletion, CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenAuditCannotBeSaved_PropagatesSoTheTransactionRollsBack()
    {
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit persistence failed."));

        var act = () => CreateHandler().HandleAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().NotContain("local-commit");
    }

    private void SetupLocalDeletion(LocalAccountDeletion deletion) =>
        localDeletion
            .Setup(x => x.DeleteAsync(IdentityUserId, It.IsAny<Func<LocalAccountDeletion, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, Func<LocalAccountDeletion, CancellationToken, Task> record, CancellationToken token) =>
            {
                calls.Add("local-delete");
                await record(deletion, token);
                calls.Add("local-commit");
                return deletion;
            });

    private DeleteAccountCommandHandler CreateHandler()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        return new DeleteAccountCommandHandler(
            currentUser.Object,
            localDeletion.Object,
            outbox.Object,
            unitOfWork.Object,
            clock.Object);
    }
}
