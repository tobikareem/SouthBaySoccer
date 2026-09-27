using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class GetMyProfileQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IConfiguredAdminPhoneNumberService configuredAdminPhoneNumberService,
    IUnitOfWork unitOfWork)
{
    public async Task<PlayerProfileModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        if (ResolvePromotedRole(profile) is { } promotedRole)
        {
            profile.Role = promotedRole;
            playerProfileRepository.Update(profile);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var emergencyContact = await playerProfileRepository.FindEmergencyContactAsync(profile.Id, cancellationToken);

        return PlayerProfileMapper.ToModel(profile, emergencyContact);
    }

    // Mirrors PickupPalUserSyncService: a configured owner number always means Owner, and the
    // promotion is reversible (an Owner whose number was removed drops to GameAdmin or Player); a
    // configured admin number promotes to GameAdmin unless an administrative role is already held.
    // Returns the role to persist, or null when nothing changes.
    private PlayerRole? ResolvePromotedRole(PlayerProfile profile)
    {
        var isConfiguredAdmin = configuredAdminPhoneNumberService.IsConfiguredAdminPhoneNumberHash(profile.PhoneNumberHash);
        if (configuredAdminPhoneNumberService.IsConfiguredOwnerPhoneNumberHash(profile.PhoneNumberHash))
        {
            return profile.Role == PlayerRole.Owner ? null : PlayerRole.Owner;
        }

        if (profile.Role == PlayerRole.Owner)
        {
            return isConfiguredAdmin ? PlayerRole.GameAdmin : PlayerRole.Player;
        }

        if (isConfiguredAdmin && !IsAdministrativeRole(profile.Role))
        {
            return PlayerRole.GameAdmin;
        }

        return null;
    }

    private static bool IsAdministrativeRole(PlayerRole role) =>
        role is PlayerRole.Owner or PlayerRole.Admin or PlayerRole.GameAdmin;
}
