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
}
