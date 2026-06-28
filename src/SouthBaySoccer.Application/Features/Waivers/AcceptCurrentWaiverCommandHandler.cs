using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Compliance;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Waivers;

public sealed class AcceptCurrentWaiverCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    IWaiverRepository waiverRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<WaiverAcceptanceModel> HandleAsync(
        AcceptCurrentWaiverCommand command,
        CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var waiver = await waiverRepository.GetCurrentPublishedWaiverAsync(cancellationToken)
            ?? throw new ApplicationNotFoundException("Current waiver document was not found.");

        var existing = await waiverRepository.FindAcceptanceAsync(profile.Id, waiver.Id, cancellationToken);
        if (existing is not null)
        {
            return new WaiverAcceptanceModel(existing.Id, waiver.Id, waiver.Version, existing.AcceptedAtUtc);
        }

        var now = clock.UtcNow;
        var acceptance = new WaiverAcceptance
        {
            Id = Guid.NewGuid(),
            PlayerProfileId = profile.Id,
            WaiverDocumentId = waiver.Id,
            AcceptedAtUtc = now,
            ContentHash = waiver.ContentHash,
        };

        await waiverRepository.AddAcceptanceAsync(acceptance, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new WaiverAcceptanceModel(acceptance.Id, waiver.Id, waiver.Version, acceptance.AcceptedAtUtc);
    }
}
