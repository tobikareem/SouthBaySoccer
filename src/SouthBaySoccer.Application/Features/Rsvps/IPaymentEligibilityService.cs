namespace SouthBaySoccer.Application.Features.Rsvps;

public interface IPaymentEligibilityService
{
    Task<PaymentEligibilityResult> CheckAsync(
        Guid playerProfileId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentEligibilityResult(bool IsEligible, string? Reason);
