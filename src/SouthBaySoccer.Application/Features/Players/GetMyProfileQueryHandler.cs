using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Players;

public sealed class GetMyProfileQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository)
{
    public async Task<PlayerProfileModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var emergencyContact = await playerProfileRepository.FindEmergencyContactAsync(profile.Id, cancellationToken);

        return PlayerProfileMapper.ToModel(profile, emergencyContact);
    }
}
