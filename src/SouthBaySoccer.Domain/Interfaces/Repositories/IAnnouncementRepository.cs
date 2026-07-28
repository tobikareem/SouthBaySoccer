using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Announcements;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for admin <see cref="Announcement"/> broadcasts. Every read is scoped to one or more
/// group chats and bounded by an explicit limit — this table grows without bound, so there is no
/// unfiltered read.
/// </summary>
public interface IAnnouncementRepository : IRepository<Announcement>
{
    /// <summary>
    /// Lists one page of a group's announcements, newest first.
    /// <para>
    /// The cursor is the composite <c>(SentAtUtc, Id)</c> of the previous page's oldest row, which
    /// is also the sort key. A timestamp alone is not enough: two announcements sharing a send time
    /// would leave one of them unreachable by any page.
    /// </para>
    /// </summary>
    /// <param name="groupChatId">The group whose feed is being read.</param>
    /// <param name="beforeUtc">Exclusive upper bound on send time, or <see langword="null"/> for the newest page.</param>
    /// <param name="beforeId">Tie-break bound applied when a row shares <paramref name="beforeUtc"/>.</param>
    /// <param name="limit">The maximum number of announcements to return.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<IReadOnlyList<AnnouncementReadModel>> ListForGroupAsync(
        Guid groupChatId,
        DateTime? beforeUtc,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the announcements the player has not yet read across every group they belong to,
    /// capped at <paramref name="cap"/> so the notification badge cannot become an unbounded scan.
    /// A player's own broadcasts never count as unread to their author, and nothing sent before
    /// they joined a group does either.
    /// </summary>
    Task<int> CountUnreadForPlayerAsync(
        Guid playerProfileId,
        int cap,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the announcements in one group that the player has not yet read.
    /// </summary>
    Task<int> CountUnreadForGroupAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the send time of the group's newest announcement, or <see langword="null"/> when the
    /// group has none. This is what a read mark is set to, rather than a wall-clock reading: the
    /// mark then describes rows that exist instead of a moment that may precede their commit.
    /// </summary>
    Task<DateTime?> FindLatestSentAtUtcAsync(
        Guid groupChatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the most recent announcements an admin sent, across the supplied groups, with the
    /// read count derived from the groups' read marks.
    /// </summary>
    Task<IReadOnlyList<SentAnnouncementReadModel>> ListSentByAuthorAsync(
        Guid authorPlayerProfileId,
        IReadOnlyCollection<Guid> groupChatIds,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for per-player, per-group announcement read marks.
/// </summary>
public interface IGroupAnnouncementReadMarkerRepository : IRepository<GroupAnnouncementReadMarker>
{
    /// <summary>
    /// Finds the player's read mark for a group, or <see langword="null"/> if they have never read it.
    /// </summary>
    Task<GroupAnnouncementReadMarker?> FindAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default);
}

/// <summary>An announcement joined to its group and author display fields, for the player feed.</summary>
public sealed record AnnouncementReadModel(
    Guid Id,
    Guid GroupChatId,
    string GroupName,
    Guid AuthorPlayerProfileId,
    string AuthorDisplayName,
    string Body,
    DateTime SentAtUtc);

/// <summary>An announcement with its delivery read receipt, for the admin's sent list.</summary>
public sealed record SentAnnouncementReadModel(
    Guid Id,
    Guid GroupChatId,
    string GroupName,
    string Body,
    DateTime SentAtUtc,
    int RecipientCount,
    int ReadCount);
