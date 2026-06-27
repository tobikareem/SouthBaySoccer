namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Contains the authenticated player identity and issued application session tokens.
/// </summary>
/// <param name="IdentityUserId">The ASP.NET Identity user id.</param>
/// <param name="PlayerProfileId">The player profile id.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
/// <param name="Roles">The role names attached to the authenticated player.</param>
/// <param name="Tokens">The issued application token set.</param>
public sealed record VerifyWhatsAppChallengeResult(
    Guid IdentityUserId,
    Guid PlayerProfileId,
    string MaskedPhoneNumber,
    IReadOnlyList<string> Roles,
    AuthenticationTokenSet Tokens);

