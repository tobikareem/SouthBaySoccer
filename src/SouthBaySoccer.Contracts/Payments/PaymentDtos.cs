namespace SouthBaySoccer.Contracts.Payments;

public sealed record CreateDropInCheckoutRequest(
    string SuccessPath,
    string CancelPath);

public sealed record CheckoutSessionResponse(
    Guid SessionId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc);

public sealed record PaymentEligibilityResponse(
    Guid SessionId,
    Guid PlayerProfileId,
    bool IsEligible,
    bool HasActiveMembership,
    bool HasSessionDropIn,
    DateTime? EligibleUntilUtc,
    string? Reason);