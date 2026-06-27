namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents the verified phone details from a consumed WhatsApp sign-in challenge.
/// </summary>
/// <param name="PhoneNumber">The verified phone number in E.164 format.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
public sealed record WhatsAppChallengeVerificationResult(
    string PhoneNumber,
    string MaskedPhoneNumber);

