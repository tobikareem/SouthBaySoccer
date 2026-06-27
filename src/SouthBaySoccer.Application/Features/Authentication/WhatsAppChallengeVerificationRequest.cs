namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents the data needed to verify a WhatsApp sign-in challenge.
/// </summary>
/// <param name="ChallengeToken">The opaque challenge token.</param>
/// <param name="CallbackUri">The callback URI bound to the challenge.</param>
public sealed record WhatsAppChallengeVerificationRequest(
    string ChallengeToken,
    string CallbackUri);

