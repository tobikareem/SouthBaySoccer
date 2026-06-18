namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Represents a player's local payment/membership state. Stripe, via verified webhooks,
/// is the source of truth; this projection is updated only from those verified events.
/// </summary>
public enum PaymentStatus
{
    /// <summary>No payment or membership record exists.</summary>
    None,

    /// <summary>The membership subscription is active and in good standing.</summary>
    Active,

    /// <summary>A subscription payment is overdue.</summary>
    PastDue,

    /// <summary>The membership subscription has been canceled.</summary>
    Canceled,

    /// <summary>The player paid as a one-time guest or drop-in rather than as a member.</summary>
    Guest
}
