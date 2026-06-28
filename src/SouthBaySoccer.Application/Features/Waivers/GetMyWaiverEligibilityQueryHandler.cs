using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Waivers;

public sealed class GetMyWaiverEligibilityQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IWaiverRepository waiverRepository)
{
    public async Task<WaiverEligibilityModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var waiver = await waiverRepository.GetCurrentPublishedWaiverAsync(cancellationToken);
        if (waiver is null)
        {
            return new WaiverEligibilityModel(false, null, null, "Current waiver is not published.");
        }

        var accepted = await waiverRepository.HasCurrentAcceptanceAsync(profile.Id, cancellationToken);
        return new WaiverEligibilityModel(
            accepted,
            waiver.Id,
            waiver.Version,
            accepted ? null : "Waiver required.");
    }
}
