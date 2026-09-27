using SouthBaySoccer.Application.Features.Authentication;

namespace SouthBaySoccer.Application.Features.Onboarding;

/// <summary>
/// Application port for the Pickup Pal account-creation and login-token surface. Every call is
/// server-to-server; the MAUI client never talks to Pickup Pal directly. Implementations must never
/// log request URIs (tokens and emails travel in them under the external contract).
/// </summary>
public interface IPickupPalOnboardingClient
{
    /// <summary>Checks a <c>!!register</c> pre-registration token before showing the details form.</summary>
    /// <param name="token">The single-use registration token from the app link.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The token status and, when valid, the phone digits it was minted for.</returns>
    Task<RegistrationTokenValidation> ValidateRegistrationTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>Determines whether Pickup Pal has no account for the email yet.</summary>
    /// <param name="email">The email to check.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Creates the Pickup Pal account bound to the registration token.</summary>
    /// <param name="registration">The details to register. The password is forwarded once and never stored.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The created Pickup Pal user.</returns>
    /// <exception cref="PickupPalOnboardingException">Pickup Pal rejected the registration.</exception>
    /// <exception cref="ApplicationServiceUnavailableException">Pickup Pal could not be reached.</exception>
    Task<PickupPalUser> RegisterWithTokenAsync(
        PickupPalRegistrationRequest registration,
        CancellationToken cancellationToken = default);

    /// <summary>Redeems a <c>!!login</c> token and returns the user it was minted for.</summary>
    /// <param name="token">The single-use login token from the app link.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <exception cref="PickupPalOnboardingException">The token is invalid or expired.</exception>
    /// <exception cref="ApplicationServiceUnavailableException">Pickup Pal could not be reached.</exception>
    Task<PickupPalUser> RedeemLoginTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Deletes (or anonymizes) the Pickup Pal user. A user that no longer exists counts as deleted.</summary>
    /// <param name="pickupPalUserId">The Pickup Pal user id.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <exception cref="ApplicationServiceUnavailableException">Pickup Pal could not be reached or refused.</exception>
    Task DeleteUserAsync(string pickupPalUserId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of validating a registration token.</summary>
public enum RegistrationTokenStatus
{
    /// <summary>The token is live and may be redeemed.</summary>
    Valid,

    /// <summary>The token is unknown or already redeemed.</summary>
    Invalid,

    /// <summary>The token has passed its 15-minute lifetime.</summary>
    Expired,
}

/// <summary>Result of validating a registration token.</summary>
/// <param name="Status">The token status.</param>
/// <param name="PhoneNumberDigits">The digits-only phone the token was minted for, when valid and known.</param>
public sealed record RegistrationTokenValidation(RegistrationTokenStatus Status, string? PhoneNumberDigits);

/// <summary>Details forwarded to Pickup Pal to create a WhatsApp-linked account.</summary>
/// <param name="Token">The single-use registration token.</param>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name.</param>
/// <param name="Email">The email.</param>
/// <param name="Password">The password, forwarded once and discarded. Never persisted or logged.</param>
/// <param name="TermsVersion">The accepted terms version.</param>
/// <param name="TermsAcceptedAtUtc">When the terms were accepted (UTC).</param>
public sealed record PickupPalRegistrationRequest(
    string Token,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string TermsVersion,
    DateTime TermsAcceptedAtUtc);
