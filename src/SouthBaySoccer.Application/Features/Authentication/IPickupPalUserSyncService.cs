namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Synchronizes Pickup Pal identity data into SouthBaySoccer local identity/profile records.
/// </summary>
public interface IPickupPalUserSyncService
{
    /// <summary>
    /// Creates or updates local records for the Pickup Pal user and returns the token subject.
    /// </summary>
    Task<AuthenticationTokenSubject> SyncAsync(PickupPalUser user, CancellationToken cancellationToken = default);
}
