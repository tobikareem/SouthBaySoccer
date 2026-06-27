namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Requests a WhatsApp/Pickup Pal sign-in challenge for a phone number.
/// </summary>
/// <param name="PhoneNumber">The phone number in E.164 format.</param>
/// <param name="CallbackUri">The callback URI bound to the challenge.</param>
public sealed record RequestWhatsAppChallengeCommand(string PhoneNumber, string CallbackUri);

