namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Application port for resolving Pickup Pal users by phone number.
/// </summary>
public interface IPickupPalUserClient
{
    /// <summary>
    /// Finds a Pickup Pal user by normalized phone digits.
    /// </summary>
    Task<PickupPalUser?> FindByPhoneAsync(string phoneNumberDigits, CancellationToken cancellationToken = default);
}
