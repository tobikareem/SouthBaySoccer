namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Tracks whether a local RSVP has been mirrored onto the Pickup Pal roster of the imported game
/// it belongs to. Only sessions imported from Pickup Pal and profiles carrying a Pickup Pal user id
/// ever leave <see cref="NotApplicable"/>.
/// </summary>
public enum PickupPalSyncStatus
{
    /// <summary>The session is app-only or the player has no Pickup Pal user id; nothing to sync.</summary>
    NotApplicable,

    /// <summary>The Pickup Pal roster reflects the local RSVP state.</summary>
    Synced,

    /// <summary>The push has not succeeded yet; an outbox row will retry it.</summary>
    Pending,

    /// <summary>Pickup Pal rejected the push for a terminal reason (see the safe error code); no retry until the RSVP changes.</summary>
    Failed,
}
