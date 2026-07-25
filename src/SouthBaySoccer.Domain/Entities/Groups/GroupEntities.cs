using System;
using SouthBaySoccer.Domain.Entities.Common;

namespace SouthBaySoccer.Domain.Entities.Groups;

/// <summary>
/// Represents a Pickup Pal WhatsApp group chat mirrored from the external bot API. Players are
/// linked to one or more group chats; the group is the scope for group-filtered leaderboards.
/// </summary>
public class GroupChat : BaseEntity
{
    /// <summary>Gets or sets the stable external group chat id (the WhatsApp <c>…@g.us</c> id).</summary>
    public string ExternalId { get; set; } = string.Empty;
    /// <summary>Gets or sets the human-readable group name shown in pickers and leaderboards.</summary>
    public string GroupName { get; set; } = string.Empty;
    /// <summary>Gets or sets the linkage code used to link a user to this group via the external API.</summary>
    public string? LinkageCode { get; set; }
    /// <summary>Gets or sets the external subscription status (for example <c>SUBSCRIBED</c>).</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Gets or sets the WhatsApp member count reported by the external API.</summary>
    public int WhatsAppMemberCount { get; set; }
    /// <summary>Gets or sets the group's IANA time zone, when reported.</summary>
    public string? Timezone { get; set; }
}

/// <summary>
/// Represents a link between a player profile and a <see cref="GroupChat"/>. Mirrors WhatsApp-side
/// membership resolved through the external API; the primary link is the leaderboard default.
/// </summary>
public class PlayerGroupLink : BaseEntity
{
    /// <summary>Gets or sets the owning player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the linked group chat id.</summary>
    public Guid GroupChatId { get; set; }
    /// <summary>Gets or sets a value indicating whether this is the player's primary (default) group.</summary>
    public bool IsPrimary { get; set; }
}
