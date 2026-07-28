using System;
using System.Collections.Generic;

namespace SouthBaySoccer.Application.Features.Announcements;

/// <summary>
/// Reads one page of a group's announcement feed for the current player. The cursor is the
/// composite <c>(BeforeUtc, BeforeId)</c> of the previous page's oldest row.
/// </summary>
public sealed record GetGroupAnnouncementsQuery(Guid GroupChatId, DateTime? BeforeUtc, Guid? BeforeId, int Limit);

/// <summary>Counts every announcement the current player has not read, across all their groups.</summary>
public sealed record GetUnreadAnnouncementCountQuery;

/// <summary>Reads the current admin's recently sent announcements with their read receipts.</summary>
public sealed record GetSentAnnouncementsQuery(int Limit);

/// <summary>Broadcasts one announcement to one group the current admin belongs to.</summary>
public sealed record PostAnnouncementCommand(Guid GroupChatId, string Body, bool SendPush);

/// <summary>Marks every announcement in a group read for the current player.</summary>
public sealed record MarkGroupAnnouncementsReadCommand(Guid GroupChatId);

/// <summary>One announcement as a player sees it.</summary>
public sealed record AnnouncementSummary(
    Guid Id,
    Guid GroupChatId,
    string GroupName,
    string AuthorDisplayName,
    string Body,
    DateTime SentAtUtc,
    bool IsUnread);

/// <summary>
/// One page of a group's feed. <see cref="NextCursorUtc"/> is non-null only when more history
/// exists, so the client can stop paging without an extra empty request.
/// </summary>
public sealed record AnnouncementFeedResult(
    Guid GroupChatId,
    string GroupName,
    IReadOnlyList<AnnouncementSummary> Announcements,
    int UnreadCount,
    DateTime? NextCursorUtc,
    Guid? NextCursorId);

/// <summary>One sent announcement with the read receipt shown to its admin author.</summary>
public sealed record SentAnnouncementSummary(
    Guid Id,
    Guid GroupChatId,
    string GroupName,
    string Body,
    DateTime SentAtUtc,
    int ReadCount,
    int RecipientCount);

/// <summary>The result of marking a group read.</summary>
public sealed record MarkGroupAnnouncementsReadResult(
    Guid GroupChatId,
    DateTime ReadThroughUtc,
    int UnreadCount);
