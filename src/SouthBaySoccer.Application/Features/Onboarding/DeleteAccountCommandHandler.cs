using System.Text.Json;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Deletes the signed-in player's account. Local records are soft-deleted and every refresh token
/// revoked first; the Pickup Pal deletion is recorded in the outbox before it is attempted, so a
/// failed upstream call leaves a pending row to reconcile rather than a half-deleted account.
/// </summary>
public sealed class DeleteAccountCommandHandler(
    ICurrentUser currentUser,
    ILocalAccountDeletionService localAccountDeletionService,
    IOutboxMessageRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPickupPalOnboardingClient onboardingClient,
    ILogger<DeleteAccountCommandHandler> logger)
{
    /// <summary>Delay before an operator or processor should retry a failed upstream deletion.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    /// <summary>Deletes the current user's account locally and requests deletion on Pickup Pal.</summary>
    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();

        var deletion = await localAccountDeletionService.DeleteAsync(identityUserId, cancellationToken);
        if (deletion.PickupPalUserId is null)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = clock.UtcNow;
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = OnboardingOutboxMessages.PickupPalUserDeletionRequested,
            PayloadJson = JsonSerializer.Serialize(new
            {
                IdentityUserId = identityUserId,
                deletion.PlayerProfileId,
                deletion.PickupPalUserId,
                RequestedAtUtc = now,
            }),
            Status = OutboxMessageStatus.Pending,
            AvailableAtUtc = now,
            IdempotencyKey = $"{OnboardingOutboxMessages.PickupPalUserDeletionRequested}:{identityUserId:D}",
        };
        await outboxRepository.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await onboardingClient.DeleteUserAsync(deletion.PickupPalUserId, cancellationToken);
            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAtUtc = clock.UtcNow;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The local account is already gone and the sessions revoked; the outbox row stays
            // pending so the upstream deletion can be retried. No identifiers are logged.
            logger.LogWarning(
                "Pickup Pal account deletion failed and was left in the outbox. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
            message.Status = OutboxMessageStatus.RetryScheduled;
            message.AvailableAtUtc = clock.UtcNow.Add(RetryDelay);
        }

        message.AttemptCount += 1;
        outboxRepository.Update(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
