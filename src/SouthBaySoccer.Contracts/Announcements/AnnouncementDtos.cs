using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Contracts.Announcements;

/// <summary>A single admin announcement as a player in the group sees it.</summary>
public sealed record AnnouncementDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string AuthorDisplayName,
    string Body,
    DateTime SentAtUtc,
    bool IsUnread);

/// <summary>
/// One page of a group's announcement feed, newest first, with the caller's unread count.
/// <para>
/// <paramref name="NextCursorUtc"/> and <paramref name="NextCursorId"/> identify the oldest item in
/// this page and are echoed back as <c>before</c> and <c>beforeId</c> to fetch the next page. Both
/// are null when no more pages exist. Send both: the timestamp alone does not order two
/// announcements that share a send time, and paging on it would skip one of them permanently.
/// </para>
/// </summary>
public sealed record AnnouncementFeedResponse(
    Guid GroupId,
    string GroupName,
    IReadOnlyList<AnnouncementDto> Announcements,
    int UnreadCount,
    DateTime? NextCursorUtc,
    Guid? NextCursorId);

/// <summary>An announcement as its admin author sees it, carrying delivery read receipts.</summary>
public sealed record SentAnnouncementDto(
    Guid Id,
    Guid GroupId,
    string GroupName,
    string Body,
    DateTime SentAtUtc,
    int ReadCount,
    int RecipientCount);

/// <summary>The admin "Recently sent" list, newest first, across the groups the admin belongs to.</summary>
public sealed record SentAnnouncementsResponse(IReadOnlyList<SentAnnouncementDto> Announcements);

/// <summary>Request body for broadcasting one announcement to one group.</summary>
public sealed record PostAnnouncementRequest(string Body, bool SendPush);

/// <summary>The caller's total unread announcement count, used by the Sessions notification bell.</summary>
public sealed record UnreadAnnouncementsResponse(int UnreadCount);

/// <summary>Result of marking a group's announcements read up to and including <paramref name="ReadThroughUtc"/>.</summary>
public sealed record MarkAnnouncementsReadResponse(Guid GroupId, DateTime ReadThroughUtc, int UnreadCount);
