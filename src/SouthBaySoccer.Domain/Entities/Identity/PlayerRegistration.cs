using System;
using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Identity;

/// <summary>
/// Represents an in-app sign-up captured locally before the Pickup Pal account is created.
/// The row is written first, then Pickup Pal is called, so a failed external call never loses the
/// registration. Never stores the raw phone number, the password, or the registration token.
/// </summary>
public class PlayerRegistration : BaseEntity
{
    /// <summary>Gets or sets the player's first name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the player's last name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the email the player registered with. Never used as a security factor.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA-256 hash of the E.164 phone number proven by the WhatsApp token.</summary>
    public string PhoneNumberHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the masked phone display value.</summary>
    public string PhoneMasked { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional preferred playing position.</summary>
    public string? PreferredPosition { get; set; }

    /// <summary>Gets or sets the terms version the player accepted.</summary>
    public string TermsVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets when the player accepted the terms (UTC).</summary>
    public DateTime TermsAcceptedAtUtc { get; set; }

    /// <summary>Gets or sets how the registration was proven.</summary>
    public RegistrationSource Source { get; set; } = RegistrationSource.WhatsAppToken;

    /// <summary>Gets or sets the local-first lifecycle status.</summary>
    public PlayerRegistrationStatus Status { get; set; } = PlayerRegistrationStatus.PendingExternal;

    /// <summary>Gets or sets the Pickup Pal user id once the external account exists.</summary>
    public string? PickupPalUserId { get; set; }

    /// <summary>Gets or sets how many times the Pickup Pal call has been attempted.</summary>
    public int ExternalAttemptCount { get; set; }

    /// <summary>Gets or sets a safe, non-personal failure code from the last Pickup Pal attempt.</summary>
    public string? LastExternalError { get; set; }

    /// <summary>Gets or sets when the registration completed (UTC).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Gets a value indicating whether the Pickup Pal account still needs to be created.</summary>
    public bool IsAwaitingExternal =>
        Status is PlayerRegistrationStatus.PendingExternal or PlayerRegistrationStatus.ExternalFailed;
}
