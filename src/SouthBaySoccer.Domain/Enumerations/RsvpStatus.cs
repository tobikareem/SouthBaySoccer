namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Represents a player's RSVP state for a given session, through to game-day attendance.
/// </summary>
public enum RsvpStatus
{
    /// <summary>The player has confirmed they are attending.</summary>
    Going,

    /// <summary>The player has tentatively indicated they may attend.</summary>
    Maybe,

    /// <summary>The player has declined to attend.</summary>
    NotGoing,

    /// <summary>The player is on the waitlist because the session is at capacity.</summary>
    Waitlisted,

    /// <summary>The player has been checked in on game day.</summary>
    CheckedIn,

    /// <summary>The player confirmed attendance but did not show up.</summary>
    NoShow
}
