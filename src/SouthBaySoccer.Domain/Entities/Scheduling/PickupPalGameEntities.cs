using SouthBaySoccer.Domain.Entities.Common;

namespace SouthBaySoccer.Domain.Entities.Scheduling;

/// <summary>
/// Represents the sanitized record of one Pickup Pal active game imported into a session.
/// Privacy: only fields free of phone-bearing identifiers are stored — never WhatsApp JIDs,
/// group ids, or subscriber ids.
/// </summary>
public class PickupPalGameSnapshot : BaseEntity
{
    /// <summary>Gets or sets the stable Pickup Pal game id this snapshot mirrors.</summary>
    public string PickupPalGameId { get; set; } = string.Empty;

    /// <summary>Gets or sets the session this game was consolidated into.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Gets or sets when this snapshot was captured from Pickup Pal.</summary>
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>Gets or sets the game start instant reported by Pickup Pal.</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>Gets or sets the free-form game location reported by Pickup Pal.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum player count reported by Pickup Pal.</summary>
    public int MaxPlayers { get; set; }

    /// <summary>Gets or sets the Pickup Pal game status (for example "active").</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the WhatsApp group display name the game belongs to.</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Gets or sets the sanitized JSON copy of the imported game payload.</summary>
    public string SanitizedGameJson { get; set; } = string.Empty;
}

/// <summary>
/// Represents one Pickup Pal participant on an imported session's going list or waitlist.
/// Participants carry display names only; they are not linked to player profiles.
/// </summary>
public class PickupPalGameParticipant : BaseEntity
{
    /// <summary>Gets or sets the session the participant belongs to.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Gets or sets the player profile this participant was matched or created as.</summary>
    public Guid? PlayerProfileId { get; set; }

    /// <summary>Gets or sets the stable Pickup Pal participant id.</summary>
    public string PickupPalParticipantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the participant's WhatsApp display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Pickup Pal marks the participant as a guest.</summary>
    public bool IsGuest { get; set; }

    /// <summary>Gets or sets a value indicating whether the participant is on the waitlist.</summary>
    public bool IsWaitlist { get; set; }

    /// <summary>Gets or sets the participant's join order within the game.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Gets or sets when the participant joined the game on Pickup Pal.</summary>
    public DateTime JoinedAtUtc { get; set; }
}
