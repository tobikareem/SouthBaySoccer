namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Represents the application identity resolved from a verified WhatsApp phone number.
/// </summary>
/// <param name="IdentityUserId">The ASP.NET Identity user id.</param>
/// <param name="PlayerProfileId">The player profile id.</param>
/// <param name="MaskedPhoneNumber">The masked phone number display value.</param>
/// <param name="Roles">The role names attached to the player.</param>
public sealed record WhatsAppIdentity(
    Guid IdentityUserId,
    Guid PlayerProfileId,
    string MaskedPhoneNumber,
    IReadOnlyList<string> Roles);

