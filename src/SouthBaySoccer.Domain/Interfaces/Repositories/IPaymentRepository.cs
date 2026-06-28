using SouthBaySoccer.Domain.Entities.Payments;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Reads verified payment eligibility projections.</summary>
public interface IPaymentRepository
{
    /// <summary>Gets membership or session-specific drop-in eligibility for a player.</summary>
    Task<PaymentEligibilityProjection> GetEligibilityAsync(
        Guid playerProfileId,
        Guid sessionId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the provider customer reference for a player profile.</summary>
    Task<StripeCustomerReference?> FindStripeCustomerReferenceAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents verified payment eligibility derived from local webhook projections.</summary>
public sealed record PaymentEligibilityProjection(
    bool HasActiveMembership,
    bool HasSessionDropIn,
    DateTime? EligibleUntilUtc);
