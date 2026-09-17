namespace SouthBaySoccer.Domain.Enumerations;

/// <summary>
/// Lifecycle of a player's membership in a WhatsApp group as recorded in our own database. Only
/// <see cref="Approved"/> memberships grant the group-scoped actions (RSVP, waitlist, self
/// check-in, claim); everything else is view-only.
/// </summary>
public enum GroupMembershipStatus
{
    /// <summary>The player asked to join and a group admin has not decided yet.</summary>
    Pending,

    /// <summary>The player is a member of the group.</summary>
    Approved,

    /// <summary>A group admin declined the request; the player may ask again later.</summary>
    Declined,

    /// <summary>The player left, or a group admin removed them; the player may ask again later.</summary>
    Removed,
}

/// <summary>Role a member holds inside one group. Group admins approve, decline, and remove members.</summary>
public enum GroupMemberRole
{
    /// <summary>An ordinary member.</summary>
    Member,

    /// <summary>A group admin, appointed by a super admin.</summary>
    Admin,
}

/// <summary>How a membership row came to exist.</summary>
public enum GroupMembershipSource
{
    /// <summary>Pickup Pal already listed the player in the WhatsApp group, so the membership was approved automatically.</summary>
    WhatsApp,

    /// <summary>The player asked to join from the app and a group admin decides.</summary>
    Request,

    /// <summary>A super admin added the player straight into the group.</summary>
    SuperAdmin,
}
