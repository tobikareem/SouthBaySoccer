using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed class PlayerSessionEligibilityService(
    IWaiverRepository waiverRepository,
    IPaymentEligibilityService paymentEligibilityService) : IPlayerSessionEligibilityService
{
    public async Task<PlayerSessionEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await waiverRepository.HasCurrentAcceptanceAsync(playerProfileId, cancellationToken))
        {
            return new PlayerSessionEligibilityResult(false, "Waiver required.");
        }

        var payment = await paymentEligibilityService.CheckAsync(playerProfileId, sessionId, cancellationToken);
        return payment.IsEligible
            ? new PlayerSessionEligibilityResult(true, null)
            : new PlayerSessionEligibilityResult(false, payment.Reason ?? "Payment required.");
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> CheckManyAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<Guid, bool>(playerProfileIds.Count);
        if (playerProfileIds.Count == 0)
        {
            return results;
        }

        var acceptedIds = (await waiverRepository.ListPlayerIdsWithCurrentAcceptanceAsync(
                playerProfileIds,
                cancellationToken))
            .ToHashSet();

        foreach (var playerProfileId in playerProfileIds.Distinct())
        {
            results[playerProfileId] = acceptedIds.Contains(playerProfileId)
                && (await paymentEligibilityService.CheckAsync(playerProfileId, sessionId, cancellationToken)).IsEligible;
        }

        return results;
    }
}
