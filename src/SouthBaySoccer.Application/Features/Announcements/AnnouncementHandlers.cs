using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Announcements;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Announcements;

/// <summary>
/// Resolves the caller's player profile and proves they belong to a group before any announcement
/// is read or written.
/// <para>
/// Group membership is a security boundary here, not a filter: announcements are private to their
/// group. A caller who is not a member is answered with "not found" rather than "forbidden" so the
/// API never confirms that a group they cannot see exists.
/// </para>
/// </summary>
internal static class AnnouncementAccess
{
    public static async Task<Guid> RequireProfileIdAsync(
        ICurrentUser currentUser,
        IPlayerProfileRepository playerProfileRepository,
        CancellationToken cancellationToken)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");

        return profile.Id;
    }

    public static async Task<PlayerGroupLink> RequireGroupMembershipAsync(
        IPlayerGroupLinkRepository playerGroupLinkRepository,
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken) =>
        await playerGroupLinkRepository.FindLinkAsync(playerProfileId, groupChatId, cancellationToken)
        ?? throw new ApplicationNotFoundException("Group chat was not found.");
}

/// <summary>
/// Returns one page of a group's announcement feed for the current player, newest first.
/// </summary>
public sealed class GetGroupAnnouncementsQueryHandler(
    IValidator<GetGroupAnnouncementsQuery> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IGroupChatRepository groupChatRepository,
    IAnnouncementRepository announcementRepository,
    IGroupAnnouncementReadMarkerRepository readMarkerRepository)
{
    public async Task<AnnouncementFeedResult> HandleAsync(
        GetGroupAnnouncementsQuery query,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var playerProfileId = await AnnouncementAccess.RequireProfileIdAsync(
            currentUser, playerProfileRepository, cancellationToken);
        var link = await AnnouncementAccess.RequireGroupMembershipAsync(
            playerGroupLinkRepository, playerProfileId, query.GroupChatId, cancellationToken);

        var groupChat = await groupChatRepository.GetByIdAsync(query.GroupChatId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Group chat was not found.");

        // Fetch one more row than asked for: its presence is what tells us another page exists,
        // which saves the client a trailing empty request at the end of history.
        var page = await announcementRepository.ListForGroupAsync(
            query.GroupChatId, query.BeforeUtc, query.BeforeId, query.Limit + 1, cancellationToken);

        var hasMore = page.Count > query.Limit;
        var items = hasMore ? page.Take(query.Limit).ToArray() : page;

        // One read mark for the whole page — the unread flag is computed in memory rather than
        // costing a query per announcement.
        var readMarker = await readMarkerRepository.FindAsync(
            playerProfileId, query.GroupChatId, cancellationToken);
        var lastReadAtUtc = readMarker?.LastReadAtUtc;

        var announcements = items
            .Select(announcement => new AnnouncementSummary(
                announcement.Id,
                announcement.GroupChatId,
                announcement.GroupName,
                announcement.AuthorDisplayName,
                announcement.Body,
                announcement.SentAtUtc,
                IsUnread: IsUnread(announcement, playerProfileId, lastReadAtUtc, link.CreatedAt)))
            .ToArray();

        var unreadCount = await announcementRepository.CountUnreadForGroupAsync(
            playerProfileId, query.GroupChatId, cancellationToken);

        var last = announcements.Length > 0 ? announcements[^1] : null;

        return new AnnouncementFeedResult(
            query.GroupChatId,
            groupChat.GroupName,
            announcements,
            unreadCount,
            hasMore && last is not null ? last.SentAtUtc : null,
            hasMore && last is not null ? last.Id : null);
    }

    /// <summary>
    /// An announcement is unread when someone else sent it, after this player joined the group, and
    /// after the point they have read through. The join floor is what stops a new member inheriting
    /// the group's entire back catalogue as unread.
    /// </summary>
    private static bool IsUnread(
        AnnouncementReadModel announcement,
        Guid playerProfileId,
        DateTime? lastReadAtUtc,
        DateTime joinedAtUtc) =>
        announcement.AuthorPlayerProfileId != playerProfileId
        && announcement.SentAtUtc > joinedAtUtc
        && (lastReadAtUtc is null || announcement.SentAtUtc > lastReadAtUtc.Value);
}

/// <summary>
/// Returns the current player's total unread announcement count for the notification bell.
/// </summary>
public sealed class GetUnreadAnnouncementCountQueryHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IAnnouncementRepository announcementRepository)
{
    /// <summary>
    /// Ceiling on the badge count. The bell renders "99+" past this, so counting further would cost
    /// a scan nobody can see.
    /// </summary>
    public const int UnreadBadgeCap = 99;

    public async Task<int> HandleAsync(
        GetUnreadAnnouncementCountQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;
        var playerProfileId = await AnnouncementAccess.RequireProfileIdAsync(
            currentUser, playerProfileRepository, cancellationToken);

        return await announcementRepository.CountUnreadForPlayerAsync(
            playerProfileId, UnreadBadgeCap, cancellationToken);
    }
}

/// <summary>
/// Returns the current admin's recently sent announcements with their read receipts. Read counts
/// are admin-facing only and are never projected into the player feed.
/// </summary>
public sealed class GetSentAnnouncementsQueryHandler(
    IValidator<GetSentAnnouncementsQuery> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IAnnouncementRepository announcementRepository)
{
    public async Task<IReadOnlyList<SentAnnouncementSummary>> HandleAsync(
        GetSentAnnouncementsQuery query,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);

        var playerProfileId = await AnnouncementAccess.RequireProfileIdAsync(
            currentUser, playerProfileRepository, cancellationToken);

        // Scoped to the admin's current groups as well as their authorship, so leaving a group also
        // ends visibility of what they sent to it.
        var links = await playerGroupLinkRepository.ListByPlayerAsync(playerProfileId, cancellationToken);
        var groupChatIds = links.Select(link => link.GroupChatId).ToArray();
        if (groupChatIds.Length == 0)
        {
            return [];
        }

        var sent = await announcementRepository.ListSentByAuthorAsync(
            playerProfileId, groupChatIds, query.Limit, cancellationToken);

        return sent
            .Select(announcement => new SentAnnouncementSummary(
                announcement.Id,
                announcement.GroupChatId,
                announcement.GroupName,
                announcement.Body,
                announcement.SentAtUtc,
                announcement.ReadCount,
                announcement.RecipientCount))
            .ToArray();
    }
}

/// <summary>
/// Broadcasts one announcement to one group. The admin must belong to the group they are posting
/// to — an admin of one crew cannot broadcast into another.
/// </summary>
public sealed class PostAnnouncementCommandHandler(
    IValidator<PostAnnouncementCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IGroupChatRepository groupChatRepository,
    IAnnouncementRepository announcementRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<SentAnnouncementSummary> HandleAsync(
        PostAnnouncementCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var playerProfileId = await AnnouncementAccess.RequireProfileIdAsync(
            currentUser, playerProfileRepository, cancellationToken);
        await AnnouncementAccess.RequireGroupMembershipAsync(
            playerGroupLinkRepository, playerProfileId, command.GroupChatId, cancellationToken);

        var groupChat = await groupChatRepository.GetByIdAsync(command.GroupChatId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Group chat was not found.");

        // The denominator of the read receipt must count the same population as the numerator:
        // players linked to the group in *our* database, excluding the author, since those are the
        // people who can actually receive an in-app announcement. The externally reported WhatsApp
        // roster size is a different, larger population and would make "seen by 12 of 8" possible.
        var recipientCount = await playerGroupLinkRepository.CountMembersAsync(
            command.GroupChatId, playerProfileId, cancellationToken);

        var announcement = new Announcement
        {
            GroupChatId = command.GroupChatId,
            AuthorPlayerProfileId = playerProfileId,
            Body = command.Body.Trim(),
            SentAtUtc = clock.UtcNow,
            RecipientCount = recipientCount,
            PushRequested = command.SendPush,
        };

        await announcementRepository.AddAsync(announcement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SentAnnouncementSummary(
            announcement.Id,
            announcement.GroupChatId,
            groupChat.GroupName,
            announcement.Body,
            announcement.SentAtUtc,
            ReadCount: 0,
            announcement.RecipientCount);
    }
}

/// <summary>
/// Moves the player's read mark for a group up to now. Repeating the call is harmless, and the
/// mark only ever moves forward so a stale in-flight request cannot un-read newer announcements.
/// </summary>
public sealed class MarkGroupAnnouncementsReadCommandHandler(
    IValidator<MarkGroupAnnouncementsReadCommand> validator,
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    IPlayerGroupLinkRepository playerGroupLinkRepository,
    IGroupAnnouncementReadMarkerRepository readMarkerRepository,
    IAnnouncementRepository announcementRepository,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<MarkGroupAnnouncementsReadResult> HandleAsync(
        MarkGroupAnnouncementsReadCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var playerProfileId = await AnnouncementAccess.RequireProfileIdAsync(
            currentUser, playerProfileRepository, cancellationToken);
        await AnnouncementAccess.RequireGroupMembershipAsync(
            playerGroupLinkRepository, playerProfileId, command.GroupChatId, cancellationToken);

        // The mark is set to the newest announcement that actually exists, not to the wall clock.
        // A clock reading can land ahead of an announcement that has been stamped but not yet
        // committed — or ahead of another instance's clock — which would mark an unseen broadcast
        // read forever. A value drawn from the rows themselves cannot outrun them.
        var readThroughUtc = await announcementRepository.FindLatestSentAtUtcAsync(
            command.GroupChatId, cancellationToken) ?? clock.UtcNow;

        try
        {
            readThroughUtc = await AdvanceMarkerAsync(playerProfileId, command.GroupChatId, readThroughUtc, cancellationToken);
        }
        catch (ApplicationConflictException)
        {
            // Two first-time reads for the same player and group raced on the unique index — the
            // other one won and the mark now exists, so re-run against it. This endpoint is meant to
            // be safe to repeat, so a concurrent tap must not surface as a conflict to the player.
            readThroughUtc = await AdvanceMarkerAsync(playerProfileId, command.GroupChatId, readThroughUtc, cancellationToken);
        }

        var unreadCount = await announcementRepository.CountUnreadForGroupAsync(
            playerProfileId, command.GroupChatId, cancellationToken);

        return new MarkGroupAnnouncementsReadResult(command.GroupChatId, readThroughUtc, unreadCount);
    }

    private async Task<DateTime> AdvanceMarkerAsync(
        Guid playerProfileId,
        Guid groupChatId,
        DateTime readThroughUtc,
        CancellationToken cancellationToken)
    {
        var marker = await readMarkerRepository.FindAsync(playerProfileId, groupChatId, cancellationToken);

        if (marker is null)
        {
            await readMarkerRepository.AddAsync(
                new GroupAnnouncementReadMarker
                {
                    PlayerProfileId = playerProfileId,
                    GroupChatId = groupChatId,
                    LastReadAtUtc = readThroughUtc,
                },
                cancellationToken);
        }
        else if (marker.LastReadAtUtc < readThroughUtc)
        {
            marker.LastReadAtUtc = readThroughUtc;
            readMarkerRepository.Update(marker);
        }
        else
        {
            // The mark only ever moves forward, so an older value is discarded rather than written.
            return marker.LastReadAtUtc;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return readThroughUtc;
    }
}
