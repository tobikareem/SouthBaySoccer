using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Announcements;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class AnnouncementRepository(SouthBaySoccerDbContext dbContext) : IAnnouncementRepository
{
    public Task<Announcement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Announcements.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AnnouncementReadModel>> ListForGroupAsync(
        Guid groupChatId,
        DateTime? beforeUtc,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var announcements = dbContext.Announcements
            .AsNoTracking()
            .Where(announcement => announcement.GroupChatId == groupChatId);

        if (beforeUtc is not null)
        {
            // Matches the (SentAtUtc desc, Id desc) sort key exactly, so a shared send time still
            // yields a strict total order and no row can be skipped between pages.
            var cursorUtc = beforeUtc.Value;
            var cursorId = beforeId ?? Guid.Empty;
            announcements = announcements.Where(announcement =>
                announcement.SentAtUtc < cursorUtc
                || (announcement.SentAtUtc == cursorUtc && announcement.Id.CompareTo(cursorId) < 0));
        }

        // The group and author names are correlated subqueries rather than inner joins: an
        // announcement must stay visible even if its author's profile is soft-deleted, otherwise the
        // feed would silently disagree with the unread count and the paging look-ahead.
        return await announcements
            .OrderByDescending(announcement => announcement.SentAtUtc)
            .ThenByDescending(announcement => announcement.Id)
            .Take(limit)
            .Select(announcement => new AnnouncementReadModel(
                announcement.Id,
                announcement.GroupChatId,
                dbContext.GroupChats
                    .Where(groupChat => groupChat.Id == announcement.GroupChatId)
                    .Select(groupChat => groupChat.GroupName)
                    .FirstOrDefault() ?? string.Empty,
                announcement.AuthorPlayerProfileId,
                dbContext.PlayerProfiles
                    .Where(author => author.Id == announcement.AuthorPlayerProfileId)
                    .Select(author => author.DisplayName)
                    .FirstOrDefault() ?? string.Empty,
                announcement.Body,
                announcement.SentAtUtc))
            .ToArrayAsync(cancellationToken);
    }

    public Task<int> CountUnreadForPlayerAsync(
        Guid playerProfileId,
        int cap,
        CancellationToken cancellationToken = default) =>
        // The read mark is resolved once per group and compared as a range predicate, so this seeks
        // the (GroupChatId, SentAtUtc) index over unread rows only instead of testing every
        // announcement in the player's history. The cap bounds the badge's worst case.
        (from link in dbContext.PlayerGroupLinks.AsNoTracking()
         where link.PlayerProfileId == playerProfileId
         let watermark = dbContext.GroupAnnouncementReadMarkers
             .Where(marker => marker.PlayerProfileId == playerProfileId
                 && marker.GroupChatId == link.GroupChatId)
             .Select(marker => (DateTime?)marker.LastReadAtUtc)
             .FirstOrDefault()
         from announcement in dbContext.Announcements.AsNoTracking()
         where announcement.GroupChatId == link.GroupChatId
            && announcement.AuthorPlayerProfileId != playerProfileId
            // Joining a group does not hand a player its entire back catalogue as unread.
            && announcement.SentAtUtc > link.CreatedAt
            && (watermark == null || announcement.SentAtUtc > watermark)
         select announcement.Id)
        .Take(cap)
        .CountAsync(cancellationToken);

    public Task<int> CountUnreadForGroupAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        (from link in dbContext.PlayerGroupLinks.AsNoTracking()
         where link.PlayerProfileId == playerProfileId && link.GroupChatId == groupChatId
         let watermark = dbContext.GroupAnnouncementReadMarkers
             .Where(marker => marker.PlayerProfileId == playerProfileId
                 && marker.GroupChatId == groupChatId)
             .Select(marker => (DateTime?)marker.LastReadAtUtc)
             .FirstOrDefault()
         from announcement in dbContext.Announcements.AsNoTracking()
         where announcement.GroupChatId == groupChatId
            && announcement.AuthorPlayerProfileId != playerProfileId
            && announcement.SentAtUtc > link.CreatedAt
            && (watermark == null || announcement.SentAtUtc > watermark)
         select announcement.Id)
        .CountAsync(cancellationToken);

    public Task<DateTime?> FindLatestSentAtUtcAsync(
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.Announcements
            .AsNoTracking()
            .Where(announcement => announcement.GroupChatId == groupChatId)
            .OrderByDescending(announcement => announcement.SentAtUtc)
            .Select(announcement => (DateTime?)announcement.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SentAnnouncementReadModel>> ListSentByAuthorAsync(
        Guid authorPlayerProfileId,
        IReadOnlyCollection<Guid> groupChatIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var ids = groupChatIds as IList<Guid> ?? groupChatIds.ToArray();

        // The read count is a correlated subquery, so the whole list — however many rows — costs a
        // single query rather than one count per announcement. It counts only players still linked
        // to the group, so numerator and snapshotted denominator describe the same population.
        return await (
            from announcement in dbContext.Announcements.AsNoTracking()
            join groupChat in dbContext.GroupChats on announcement.GroupChatId equals groupChat.Id
            where announcement.AuthorPlayerProfileId == authorPlayerProfileId
               && ids.Contains(announcement.GroupChatId)
            orderby announcement.SentAtUtc descending, announcement.Id descending
            select new SentAnnouncementReadModel(
                announcement.Id,
                announcement.GroupChatId,
                groupChat.GroupName,
                announcement.Body,
                announcement.SentAtUtc,
                announcement.RecipientCount,
                dbContext.GroupAnnouncementReadMarkers.Count(marker =>
                    marker.GroupChatId == announcement.GroupChatId
                    && marker.PlayerProfileId != authorPlayerProfileId
                    && marker.LastReadAtUtc >= announcement.SentAtUtc
                    && dbContext.PlayerGroupLinks.Any(link =>
                        link.PlayerProfileId == marker.PlayerProfileId
                        && link.GroupChatId == announcement.GroupChatId))))
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Announcement entity, CancellationToken cancellationToken = default) =>
        await dbContext.Announcements.AddAsync(entity, cancellationToken);

    public void Update(Announcement entity) =>
        dbContext.Announcements.Update(entity);

    public void SoftDelete(Announcement entity)
    {
        entity.IsDeleted = true;
        dbContext.Announcements.Update(entity);
    }
}

internal sealed class GroupAnnouncementReadMarkerRepository(SouthBaySoccerDbContext dbContext)
    : IGroupAnnouncementReadMarkerRepository
{
    public Task<GroupAnnouncementReadMarker?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.GroupAnnouncementReadMarkers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<GroupAnnouncementReadMarker?> FindAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.GroupAnnouncementReadMarkers.SingleOrDefaultAsync(
            x => x.PlayerProfileId == playerProfileId && x.GroupChatId == groupChatId,
            cancellationToken);

    public async Task AddAsync(GroupAnnouncementReadMarker entity, CancellationToken cancellationToken = default) =>
        await dbContext.GroupAnnouncementReadMarkers.AddAsync(entity, cancellationToken);

    public void Update(GroupAnnouncementReadMarker entity) =>
        dbContext.GroupAnnouncementReadMarkers.Update(entity);

    public void SoftDelete(GroupAnnouncementReadMarker entity)
    {
        entity.IsDeleted = true;
        dbContext.GroupAnnouncementReadMarkers.Update(entity);
    }
}
