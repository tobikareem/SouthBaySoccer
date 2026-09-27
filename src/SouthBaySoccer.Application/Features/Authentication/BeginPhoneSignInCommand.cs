namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Starts phone sign-in by confirming the phone number exists in Pickup Pal. Tokens are issued
/// here only when WhatsApp verification is not required for the number; otherwise a pending
/// sign-in is recorded and the player must complete the <c>!!login</c> link.
/// </summary>
/// <param name="PhoneNumber">The phone number submitted by the client.</param>
public sealed record BeginPhoneSignInCommand(string PhoneNumber);

/// <summary>Result of starting phone sign-in.</summary>
/// <param name="VerificationRequired">Whether the player must complete the WhatsApp login link.</param>
/// <param name="PhoneMasked">The masked matched phone, when verification is required.</param>
/// <param name="DisplayName">The matched account's display name, when verification is required.</param>
/// <param name="Tokens">The issued tokens, only when verification is not required.</param>
public sealed record PhoneSignInStart(
    bool VerificationRequired,
    string? PhoneMasked,
    string? DisplayName,
    AuthenticationTokenSet? Tokens);
