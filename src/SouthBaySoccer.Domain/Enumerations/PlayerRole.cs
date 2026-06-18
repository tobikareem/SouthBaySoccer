namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Identifies the role a player holds, which governs role- and policy-based access.
/// </summary>
public enum PlayerRole
{
    /// <summary>The owner of the organization, with full administrative control.</summary>
    Owner,

    /// <summary>A general administrator with broad management permissions.</summary>
    Admin,

    /// <summary>An administrator scoped to running game days and sessions.</summary>
    GameAdmin,

    /// <summary>A captain who leads a side drafted on a game day.</summary>
    Captain,

    /// <summary>A standard registered player.</summary>
    Player,

    /// <summary>A guest or drop-in participant without a full account.</summary>
    Guest
}
