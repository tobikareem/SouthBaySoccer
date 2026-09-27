namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Where a session's Pickup Pal game came from. Only <see cref="CreatedByApp"/> games are ever
/// created, updated, or terminated on Pickup Pal by this application; <see cref="Imported"/> games
/// belong to Pickup Pal and are mirrored locally by the active-games import.
/// </summary>
public enum PickupPalOrigin
{
    /// <summary>The session has no Pickup Pal game (app-only).</summary>
    None,

    /// <summary>The session mirrors a game Pickup Pal created; Pickup Pal owns it.</summary>
    Imported,

    /// <summary>The application created the Pickup Pal game when the session was published; the app owns it.</summary>
    CreatedByApp,
}
