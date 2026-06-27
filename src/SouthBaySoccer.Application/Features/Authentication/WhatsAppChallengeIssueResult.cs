namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents provider-safe metadata for a created WhatsApp sign-in challenge.
/// </summary>
/// <param name="ChallengeId">The provider-safe challenge identifier.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
/// <param name="ExpiresAtUtc">The UTC challenge expiration timestamp.</param>
public sealed record WhatsAppChallengeIssueResult(
    string ChallengeId,
    string MaskedPhoneNumber,
    DateTime ExpiresAtUtc);

