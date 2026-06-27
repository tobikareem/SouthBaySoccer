namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Contains safe challenge metadata returned after requesting a WhatsApp sign-in challenge.
/// </summary>
/// <param name="ChallengeId">The provider-safe challenge identifier.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
/// <param name="ExpiresAtUtc">The UTC challenge expiration timestamp.</param>
public sealed record RequestWhatsAppChallengeResult(
    string ChallengeId,
    string MaskedPhoneNumber,
    DateTime ExpiresAtUtc);

