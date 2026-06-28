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
    /// <param name="request">The checkout request details.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The provider checkout session details.</returns>
    Task<CheckoutSessionModel> CreateCheckoutSessionAsync(
        CheckoutSessionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CheckoutSessionRequest(
    Guid PlayerProfileId,
    Guid? SessionId,
    string CustomerReference,
    string Mode,
    string PriceCode,
    string SuccessPath,
    string CancelPath);

public sealed record CheckoutSessionModel(
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc);