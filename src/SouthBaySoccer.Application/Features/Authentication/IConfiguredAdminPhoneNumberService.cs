namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Identifies phone numbers that should receive local game-admin privileges.
/// </summary>
public interface IConfiguredAdminPhoneNumberService
{
    /// <summary>
    /// Determines whether the supplied phone number belongs to a configured game admin.
    /// </summary>
    bool IsConfiguredAdminPhoneNumber(string phoneNumber);

    /// <summary>
    /// Determines whether the supplied persisted phone hash belongs to a configured game admin.
    /// </summary>
    bool IsConfiguredAdminPhoneNumberHash(string? phoneNumberHash);
}
