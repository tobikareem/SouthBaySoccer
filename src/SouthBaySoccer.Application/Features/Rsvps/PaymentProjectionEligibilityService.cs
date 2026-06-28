using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed class PaymentProjectionEligibilityService(
    IClock clock,
    IPaymentRepository paymentRepository) : IPaymentEligibilityService
{
    public async Task<PaymentEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var projection = await paymentRepository.GetEligibilityAsync(playerProfileId, sessionId, clock.UtcNow, cancellationToken);
        if (projection.HasActiveMembership)
        {
            return new PaymentEligibilityResult(true, null);
        }

        if (projection.HasSessionDropIn)
        {
            return new PaymentEligibilityResult(true, null);
        }

        return new PaymentEligibilityResult(false, "Payment required.");
    }
}