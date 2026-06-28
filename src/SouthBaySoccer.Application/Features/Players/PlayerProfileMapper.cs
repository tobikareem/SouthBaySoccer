using SouthBaySoccer.Domain.Entities.Identity;

namespace SouthBaySoccer.Application.Features.Players;

internal static class PlayerProfileMapper
{
    public static PlayerProfileModel ToModel(PlayerProfile profile, EmergencyContact? emergencyContact) =>
        new(
            profile.Id,
            profile.IdentityUserId,
            profile.DisplayName,
            profile.PreferredPosition,
            profile.PhotoUri,
            profile.IsGuest,
            profile.Role.ToString(),
            emergencyContact is null
                ? null
                : new EmergencyContactView(
                    emergencyContact.Name,
                    emergencyContact.MaskedPhoneNumber,
                    emergencyContact.Relationship));
}
