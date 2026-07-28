using System;
using SouthBaySoccer.Domain.Entities.Common;

namespace SouthBaySoccer.Domain.Entities.Announcements;

/// <summary>
/// Represents one admin broadcast posted to a single group chat. Announcements are immutable once
/// sent — there is no edit path — so the body and the recipient count are snapshotted at send time
/// and never recomputed.
/// </summary>
public class Announcement : BaseEntity
{
    /// <summary>Gets or sets the group chat this announcement was broadcast to.</summary>
    public Guid GroupChatId { get; set; }

    /// <summary>Gets or sets the player profile of the admin who sent the announcement.</summary>
    public Guid AuthorPlayerProfileId { get; set; }

    /// <summary>Gets or sets the announcement body as written by the admin.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp the announcement was broadcast.</summary>
    public DateTime SentAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the group's member count captured at send time. This is the denominator of the
    /// admin's read receipt and is deliberately a snapshot: later joins and leaves must not change
    /// how many people a past broadcast was sent to.
    /// </summary>
    public int RecipientCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the admin asked for an accompanying push
    /// notification. Recording the intent is separate from delivering it.
    /// </summary>
    public bool PushRequested { get; set; }
}

/// <summary>
/// Represents how far one player has read a group's announcements — a high-water mark rather than a
/// row per player per announcement.
/// <para>
/// This is what keeps broadcasting cheap: sending is a single insert regardless of group size, and
/// "mark all read" is a single-row update. An announcement is unread for a player when it was sent
/// after that player's mark for the group. Read receipts for admins are derived by counting the
/// marks in the group that sit at or after an announcement's send time.
/// </para>
/// </summary>
public class GroupAnnouncementReadMarker : BaseEntity
{
    /// <summary>Gets or sets the player the mark belongs to.</summary>
    public Guid PlayerProfileId { get; set; }

    /// <summary>Gets or sets the group chat the mark applies to.</summary>
    public Guid GroupChatId { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp through which this player has read the group's announcements.
    /// The value only ever moves forward.
    /// </summary>
    public DateTime LastReadAtUtc { get; set; }
}
