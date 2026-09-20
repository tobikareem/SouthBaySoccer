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
/// revoked atomically with the audit/outbox intent before Pickup Pal deletion is attempted, so a
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

    /// <summary>
    /// Deletes the caller's N9ja Bay account. The Pickup Pal account is left intact unless
    /// <paramref name="alsoDeletePickupPalAccount"/> is true, because Pickup Pal accounts are used
    /// independently on their website and WhatsApp bot; Apple 5.1.1(v) only requires our data gone.
    /// Either way an audit row is written.
    /// </summary>
    public async Task HandleAsync(bool alsoDeletePickupPalAccount = false, CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();

        OutboxMessage? message = null;
        var deletion = await localAccountDeletionService.DeleteAsync(
            identityUserId,
            async (deleted, token) => message = await RecordDeletionAsync(
                identityUserId, deleted, alsoDeletePickupPalAccount, token),
            cancellationToken);
        if (message is null || message.Status == OutboxMessageStatus.Processed || deletion.PickupPalUserId is null)
        {
            return;
        }

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

    private async Task<OutboxMessage?> RecordDeletionAsync(
        Guid identityUserId,
        LocalAccountDeletion deletion,
        bool alsoDeletePickupPalAccount,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (deletion.PickupPalUserId is null || !alsoDeletePickupPalAccount)
        {
            var auditKey = $"{OnboardingOutboxMessages.N9jaBayAccountDeleted}:{identityUserId:D}";
            if (await outboxRepository.FindByIdempotencyKeyAsync(auditKey, cancellationToken) is null)
            {
                await outboxRepository.AddAsync(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    MessageType = OnboardingOutboxMessages.N9jaBayAccountDeleted,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        IdentityUserId = identityUserId,
                        deletion.PlayerProfileId,
                        deletion.PickupPalUserId,
                        PickupPalAccountRetained = true,
                        DeletedAtUtc = now,
                    }),
                    Status = OutboxMessageStatus.Processed,
                    AvailableAtUtc = now,
                    ProcessedAtUtc = now,
                    IdempotencyKey = auditKey,
                }, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }

        // One outbox row per identity user: a second delete (double tap, retried request) reuses the
        // existing row instead of tripping the unique idempotency-key index after local deletion.
        var idempotencyKey = $"{OnboardingOutboxMessages.PickupPalUserDeletionRequested}:{identityUserId:D}";
        var message = await outboxRepository.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (message is null)
        {
            message = new OutboxMessage
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
                IdempotencyKey = idempotencyKey,
            };
            await outboxRepository.AddAsync(message, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return message;
    }
}
