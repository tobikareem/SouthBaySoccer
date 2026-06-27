using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Payments;

/// <summary>Represents a local profile to Stripe customer mapping.</summary>
public class StripeCustomerReference : BaseEntity
{
    /// <summary>Gets or sets the player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the Stripe customer id.</summary>
    public string StripeCustomerId { get; set; } = string.Empty;
}

/// <summary>Represents the local membership eligibility projection from Stripe.</summary>
public class Membership : BaseEntity
{
    /// <summary>Gets or sets the player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the payment status projection.</summary>
    public PaymentStatus PaymentStatus { get; set; }
    /// <summary>Gets or sets the membership eligibility end timestamp.</summary>
    public DateTime? EligibleUntilUtc { get; set; }
    /// <summary>Gets or sets the provider version timestamp used for out-of-order protection.</summary>
    public DateTime? ProviderVersionUtc { get; set; }
    /// <summary>Gets or sets the Stripe subscription id.</summary>
    public string? StripeSubscriptionId { get; set; }
}

/// <summary>Represents an immutable payment ledger row from verified provider events.</summary>
public class PaymentLedgerEntry : BaseEntity
{
    /// <summary>Gets or sets the player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the optional session id for drop-in eligibility.</summary>
    public Guid? SessionId { get; set; }
    /// <summary>Gets or sets the processed webhook event id.</summary>
    public Guid? ProcessedWebhookEventId { get; set; }
    /// <summary>Gets or sets the ledger entry type.</summary>
    public PaymentLedgerEntryType EntryType { get; set; }
    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = "Stripe";
    /// <summary>Gets or sets the provider reference id.</summary>
    public string ProviderReferenceId { get; set; } = string.Empty;
    /// <summary>Gets or sets the amount in minor currency units.</summary>
    public long AmountMinor { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>Gets or sets when the payment event occurred.</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Represents an immutable webhook idempotency record.</summary>
public class ProcessedWebhookEvent : BaseEntity
{
    /// <summary>Gets or sets the provider name.</summary>
    public string Provider { get; set; } = "Stripe";
    /// <summary>Gets or sets the provider event id.</summary>
    public string ProviderEventId { get; set; } = string.Empty;
    /// <summary>Gets or sets the event type.</summary>
    public string EventType { get; set; } = string.Empty;
    /// <summary>Gets or sets when the provider created the event.</summary>
    public DateTime ProviderCreatedAtUtc { get; set; }
    /// <summary>Gets or sets when this app processed the event.</summary>
    public DateTime ProcessedAtUtc { get; set; }
    /// <summary>Gets or sets processing status.</summary>
    public WebhookProcessingStatus Status { get; set; }
}
