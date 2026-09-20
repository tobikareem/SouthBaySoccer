namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Application port that removes a player's local footprint: soft-deletes the profile and its
/// dependents, disables the identity user, and revokes every refresh token. Implemented in
/// Infrastructure because identity users and refresh tokens live there.
/// </summary>
public interface ILocalAccountDeletionService
{
    /// <summary>Soft-deletes the local account owned by the identity user and revokes its sessions.</summary>
    /// <param name="identityUserId">The ASP.NET Identity user id.</param>
    /// <param name="recordDeletionAsync">Idempotent database-only callback that records the audit/outbox
    /// intent in the same transaction. It may execute again on a transient SQL retry.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>What was deleted, including the Pickup Pal user id to delete upstream when linked.</returns>
    Task<LocalAccountDeletion> DeleteAsync(
        Guid identityUserId,
        Func<LocalAccountDeletion, CancellationToken, Task> recordDeletionAsync,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a local account deletion.</summary>
/// <param name="PlayerProfileId">The soft-deleted profile id, when one existed.</param>
/// <param name="PickupPalUserId">The linked Pickup Pal user id, when the account came from Pickup Pal.</param>
public sealed record LocalAccountDeletion(Guid? PlayerProfileId, string? PickupPalUserId);
