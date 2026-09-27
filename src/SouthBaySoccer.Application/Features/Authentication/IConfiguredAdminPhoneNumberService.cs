namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Identifies phone numbers that should receive local game-admin or super-admin (owner) privileges.
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

    /// <summary>
    /// Determines whether the supplied phone number belongs to a configured super admin (owner).
    /// </summary>
    bool IsConfiguredOwnerPhoneNumber(string phoneNumber);

    /// <summary>
    /// Determines whether the supplied persisted phone hash belongs to a configured super admin (owner).
    /// </summary>
    bool IsConfiguredOwnerPhoneNumberHash(string? phoneNumberHash);
}
