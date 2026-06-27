namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Verifies a WhatsApp/Pickup Pal sign-in challenge and exchanges it for application tokens.
/// </summary>
/// <param name="ChallengeToken">The opaque challenge token received from the trusted callback.</param>
/// <param name="CallbackUri">The callback URI originally bound to the challenge.</param>
public sealed record VerifyWhatsAppChallengeCommand(string ChallengeToken, string CallbackUri);

