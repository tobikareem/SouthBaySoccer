namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents the verified phone details from a consumed WhatsApp sign-in challenge.
/// </summary>
/// <param name="PhoneNumberHash">The verified phone number hash.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
public sealed record WhatsAppChallengeVerificationResult(
    string PhoneNumberHash,
    string MaskedPhoneNumber);