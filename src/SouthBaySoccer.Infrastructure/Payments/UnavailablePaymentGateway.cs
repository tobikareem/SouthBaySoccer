using SouthBaySoccer.Application.Abstractions.Payments;
using SouthBaySoccer.Application.Common;

namespace SouthBaySoccer.Infrastructure.Payments;

internal sealed class UnavailablePaymentGateway : IPaymentGateway
{
    public Task<CheckoutSessionModel> CreateCheckoutSessionAsync(
        CheckoutSessionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new ApplicationConflictException("Payment checkout provider is not configured.");
}