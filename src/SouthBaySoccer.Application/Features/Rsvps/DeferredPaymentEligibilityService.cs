namespace SouthBaySoccer.Application.Features.Rsvps;

public sealed class DeferredPaymentEligibilityService : IPaymentEligibilityService
{
    public Task<PaymentEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentEligibilityResult(true, "Payments deferred until M5."));
    }
}
