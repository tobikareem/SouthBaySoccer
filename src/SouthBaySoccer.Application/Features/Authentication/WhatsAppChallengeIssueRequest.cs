namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents the data needed by the challenge provider to create a WhatsApp sign-in challenge.
/// </summary>
/// <param name="PhoneNumber">The phone number in E.164 format.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
/// <param name="CallbackUri">The callback URI to bind to the challenge.</param>
public sealed record WhatsAppChallengeIssueRequest(
    string PhoneNumber,
    string MaskedPhoneNumber,
    string CallbackUri);

