using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PlayerGroupLinkRepository(SouthBaySoccerDbContext dbContext) : IPlayerGroupLinkRepository
{
    public Task<PlayerGroupLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PlayerGroupLink>> ListByPlayerAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        await dbContext.PlayerGroupLinks
            .Where(x => x.PlayerProfileId == playerProfileId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerGroupLink>> ListApprovedByPlayerAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        await dbContext.PlayerGroupLinks
            .Where(x => x.PlayerProfileId == playerProfileId && x.Status == GroupMembershipStatus.Approved)
            .ToArrayAsync(cancellationToken);

    public Task<bool> ExistsApprovedAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks.AnyAsync(
            x => x.PlayerProfileId == playerProfileId
                && x.GroupChatId == groupChatId
                && x.Status == GroupMembershipStatus.Approved,
            cancellationToken);

    public async Task<IReadOnlyList<PlayerGroupReadModel>> ListPlayerGroupsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        await (
            from link in dbContext.PlayerGroupLinks
            join groupChat in dbContext.GroupChats on link.GroupChatId equals groupChat.Id
            where link.PlayerProfileId == playerProfileId && link.Status == GroupMembershipStatus.Approved
            orderby link.IsPrimary descending, groupChat.GroupName, groupChat.Id
            select new PlayerGroupReadModel(
                groupChat.Id,
                groupChat.ExternalId,
                groupChat.GroupName,
                groupChat.WhatsAppMemberCount,
                link.IsPrimary))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerMembershipReadModel>> ListPlayerMembershipsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        await (
            from link in dbContext.PlayerGroupLinks.AsNoTracking()
            join groupChat in dbContext.GroupChats on link.GroupChatId equals groupChat.Id
            where link.PlayerProfileId == playerProfileId
            orderby groupChat.GroupName, groupChat.Id
            select new PlayerMembershipReadModel(
                groupChat.Id,
                groupChat.GroupName,
                link.Status,
                link.Role,
                link.Source,
                link.RequestedAtUtc,
                link.ApprovedAtUtc))
            .ToArrayAsync(cancellationToken);

    public Task<PlayerGroupLink?> FindLinkAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PlayerProfileId == playerProfileId
                    && x.GroupChatId == groupChatId
                    && x.Status == GroupMembershipStatus.Approved,
                cancellationToken);

    public Task<PlayerGroupLink?> FindMembershipAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks
            .SingleOrDefaultAsync(
                x => x.PlayerProfileId == playerProfileId && x.GroupChatId == groupChatId,
                cancellationToken);

    public async Task<IReadOnlyList<GroupMemberReadModel>> ListGroupMembersAsync(
        Guid groupChatId,
        CancellationToken cancellationToken = default)
    {
        // Ordered on an anonymous projection first: ordering after projecting into a positional
        // record is the EF pitfall noted in the games-import memory.
        var rows = await (
            from link in dbContext.PlayerGroupLinks.AsNoTracking()
            join profile in dbContext.PlayerProfiles on link.PlayerProfileId equals profile.Id
            where link.GroupChatId == groupChatId
            orderby profile.NormalizedDisplayName, profile.Id
            select new
            {
                profile.Id,
                profile.DisplayName,
                link.Status,
                link.Role,
                link.Source,
                link.RequestedAtUtc,
                link.ApprovedAtUtc,
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => new GroupMemberReadModel(
                row.Id,
                row.DisplayName,
                row.Status,
                row.Role,
                row.Source,
                row.RequestedAtUtc,
                row.ApprovedAtUtc))
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, GroupMembershipCounts>> CountByGroupAsync(
        IReadOnlyCollection<Guid> groupChatIds,
        CancellationToken cancellationToken = default)
    {
        if (groupChatIds.Count == 0)
        {
            return new Dictionary<Guid, GroupMembershipCounts>();
        }

        var idArray = groupChatIds as Guid[] ?? groupChatIds.ToArray();
        var rows = await dbContext.PlayerGroupLinks
            .AsNoTracking()
            .Where(x => idArray.Contains(x.GroupChatId)
                && (x.Status == GroupMembershipStatus.Approved || x.Status == GroupMembershipStatus.Pending))
            .GroupBy(x => x.GroupChatId)
            .Select(grouped => new
            {
                GroupChatId = grouped.Key,
                Approved = grouped.Count(x => x.Status == GroupMembershipStatus.Approved),
                Pending = grouped.Count(x => x.Status == GroupMembershipStatus.Pending),
            })
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.GroupChatId,
            row => new GroupMembershipCounts(row.Approved, row.Pending));
    }

    public Task<int> CountMembersAsync(
        Guid groupChatId,
        Guid? excludingPlayerProfileId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks
            .AsNoTracking()
            .Where(x => x.GroupChatId == groupChatId
                && x.Status == GroupMembershipStatus.Approved
                && (excludingPlayerProfileId == null || x.PlayerProfileId != excludingPlayerProfileId.Value))
            .CountAsync(cancellationToken);

    public async Task AddAsync(PlayerGroupLink entity, CancellationToken cancellationToken = default) =>
        await dbContext.PlayerGroupLinks.AddAsync(entity, cancellationToken);

    public void Update(PlayerGroupLink entity) =>
        dbContext.PlayerGroupLinks.Update(entity);

    public void SoftDelete(PlayerGroupLink entity)
    {
        entity.IsDeleted = true;
        dbContext.PlayerGroupLinks.Update(entity);
    }
}
