namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Represents a player's attendance intent for a given session. Actual attendance is tracked
/// separately through check-in and attendance-outcome records.
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
    Waitlisted
}
