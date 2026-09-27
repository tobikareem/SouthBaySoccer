using System.Text.Json;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Deletes the signed-in player's N9ja Bay account. Local records are soft-deleted, the identity
/// user anonymized, and every refresh token revoked atomically with an audit row. Pickup Pal is
/// never called: the Pickup Pal account is used independently on its website and WhatsApp bot, and
/// Apple 5.1.1(v) only requires our data gone.
/// </summary>
public sealed class DeleteAccountCommandHandler(
    ICurrentUser currentUser,
    ILocalAccountDeletionService localAccountDeletionService,
    IOutboxMessageRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Deletes the caller's N9ja Bay account and records the audit row.</summary>
    public async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();

        await localAccountDeletionService.DeleteAsync(
            identityUserId,
            (deleted, token) => RecordDeletionAsync(identityUserId, deleted, token),
            cancellationToken);
    }

    private async Task RecordDeletionAsync(
        Guid identityUserId,
        LocalAccountDeletion deletion,
        CancellationToken cancellationToken)
    {
        // One audit row per identity user: a second delete (double tap, retried request, or an
        // execution-strategy replay) reuses it instead of tripping the unique idempotency-key index.
        var auditKey = $"{OnboardingOutboxMessages.N9jaBayAccountDeleted}:{identityUserId:D}";
        if (await outboxRepository.FindByIdempotencyKeyAsync(auditKey, cancellationToken) is null)
        {
            var now = clock.UtcNow;
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
    }
}
