using System;
using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Domain.Enumerations;

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
/// The membership record between a player profile and a <see cref="GroupChat"/>: one row per
/// (player, group) pair whose <see cref="Status"/> moves through the approval lifecycle. Our
/// database is the source of truth for membership; nothing here is ever written to Pickup Pal.
/// The primary link is the leaderboard default.
/// </summary>
public class PlayerGroupLink : BaseEntity
{
    /// <summary>Gets or sets the owning player profile id.</summary>
    public Guid PlayerProfileId { get; set; }
    /// <summary>Gets or sets the linked group chat id.</summary>
    public Guid GroupChatId { get; set; }
    /// <summary>Gets or sets a value indicating whether this is the player's primary (default) group.</summary>
    public bool IsPrimary { get; set; }
    /// <summary>Gets or sets where the membership stands in the approval lifecycle.</summary>
    public GroupMembershipStatus Status { get; set; } = GroupMembershipStatus.Pending;
    /// <summary>Gets or sets the member's role inside the group.</summary>
    public GroupMemberRole Role { get; set; } = GroupMemberRole.Member;
    /// <summary>Gets or sets how the membership came to exist.</summary>
    public GroupMembershipSource Source { get; set; } = GroupMembershipSource.Request;
    /// <summary>Gets or sets when the player asked to join (or was added), in UTC.</summary>
    public DateTime RequestedAtUtc { get; set; }
    /// <summary>Gets or sets when the membership was approved, in UTC.</summary>
    public DateTime? ApprovedAtUtc { get; set; }
    /// <summary>Gets or sets the profile that approved the membership, when a person did.</summary>
    public Guid? ApprovedByPlayerProfileId { get; set; }
    /// <summary>Gets or sets when the membership was removed or declined, in UTC.</summary>
    public DateTime? RemovedAtUtc { get; set; }
    /// <summary>Gets or sets the profile that removed or declined the membership (the player themselves when they left).</summary>
    public Guid? RemovedByPlayerProfileId { get; set; }
}
