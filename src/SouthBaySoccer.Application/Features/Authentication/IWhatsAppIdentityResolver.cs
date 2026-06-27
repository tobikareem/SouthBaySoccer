namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Application port for resolving a verified WhatsApp phone number to an application identity.
/// </summary>
public interface IWhatsAppIdentityResolver
{
    /// <summary>
    /// Finds the application identity associated with the verified phone number.
    /// </summary>
    /// <param name="phoneNumber">The verified phone number in E.164 format.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The resolved identity, or <see langword="null"/> when no player can sign in with the phone number.</returns>
    Task<WhatsAppIdentity?> FindByVerifiedPhoneNumberAsync(
        string phoneNumber,
        CancellationToken cancellationToken = default);
}

