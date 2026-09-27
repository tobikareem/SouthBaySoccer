using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Entities.Identity;

/// <summary>Represents a player or guest profile and is the business anchor for RSVP, payment, and stats.</summary>
public class PlayerProfile : BaseEntity
{
    /// <summary>Gets or sets the optional ASP.NET Identity user id for registered players.</summary>
    public Guid? IdentityUserId { get; set; }
    /// <summary>Gets or sets the stable Pickup Pal user id for linked registered players.</summary>
    public string? PickupPalUserId { get; set; }
    /// <summary>Gets or sets the display name shown in rosters and stats.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Gets or sets the normalized display name used for search.</summary>
    public string NormalizedDisplayName { get; set; } = string.Empty;
    /// <summary>Gets or sets the preferred playing position.</summary>
    public string PreferredPosition { get; set; } = string.Empty;
    /// <summary>Gets or sets the optional phone number hash.</summary>
    public string? PhoneNumberHash { get; set; }
    /// <summary>Gets or sets the optional masked phone display value.</summary>
    public string? MaskedPhoneNumber { get; set; }
    /// <summary>
    /// Gets or sets the optional WhatsApp identity hash used to deduplicate players imported from
    /// Pickup Pal games. Only the hash is ever stored — never a raw WhatsApp identifier.
    /// </summary>
    public string? WhatsAppJidHash { get; set; }
    /// <summary>Gets or sets the optional profile photo URI.</summary>
    public string? PhotoUri { get; set; }
    /// <summary>Gets or sets a value indicating whether this profile is a guest.</summary>
    public bool IsGuest { get; set; }
    /// <summary>Gets or sets the player role projection.</summary>
    public PlayerRole Role { get; set; } = PlayerRole.Player;
}

/// <summary>Represents a private emergency contact for a player profile.</summary>
public class EmergencyContact : BaseEntity
{
    /// <summary>Gets or sets the owning player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the contact name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the phone hash.</summary>
    public string PhoneNumberHash { get; set; } = string.Empty;
    /// <summary>Gets or sets the masked phone display value.</summary>
    public string MaskedPhoneNumber { get; set; } = string.Empty;
    /// <summary>Gets or sets the relationship label.</summary>
    public string? Relationship { get; set; }
}

/// <summary>Represents an auditable merge from a guest profile into a claimed profile.</summary>
public class ProfileMerge : BaseEntity
{
    /// <summary>Gets or sets the source profile id.</summary>
    public Guid SourcePlayerProfileId { get; set; }
    /// <summary>Gets or sets the target profile id.</summary>
    public Guid TargetPlayerProfileId { get; set; }
    /// <summary>Gets or sets the merge status.</summary>
    public ProfileMergeStatus Status { get; set; }
    /// <summary>Gets or sets when the merge completed.</summary>
    public DateTime? MergedAtUtc { get; set; }
    /// <summary>Gets or sets the actor type that performed the merge.</summary>
    public AuditActorType MergedByActorType { get; set; }
    /// <summary>Gets or sets the optional actor id.</summary>
    public string? MergedByActorId { get; set; }
}
