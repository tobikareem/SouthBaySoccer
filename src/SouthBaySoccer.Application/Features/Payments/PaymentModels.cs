namespace SouthBaySoccer.Application.Features.Payments;

public sealed record CreateSessionDropInCheckoutCommand(
    Guid SessionId,
    string SuccessPath,
    string CancelPath);

public sealed record CheckoutSessionResultModel(
    Guid SessionId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc);

public sealed record PaymentEligibilityModel(
    Guid SessionId,
    Guid PlayerProfileId,
    bool IsEligible,
    bool HasActiveMembership,
    bool HasSessionDropIn,
    DateTime? EligibleUntilUtc,
    string? Reason);