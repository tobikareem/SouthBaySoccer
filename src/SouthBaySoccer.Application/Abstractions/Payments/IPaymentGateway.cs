namespace SouthBaySoccer.Application.Abstractions.Payments;

/// <summary>
/// Application port over the payment provider (Stripe in Infrastructure). The gateway
/// initiates provider-hosted checkout; verified webhooks remain the source of truth for
/// payment state, so this port never asserts a payment as settled.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a provider-hosted checkout session and returns the short-lived URL the
    /// client opens to complete payment.
    /// </summary>
    /// <param name="customerReference">The provider customer reference the checkout is for.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The short-lived checkout URL.</returns>
    Task<string> CreateCheckoutSessionAsync(string customerReference, CancellationToken cancellationToken = default);
}
