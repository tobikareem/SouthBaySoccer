using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Groups;
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

    public Task<bool> ExistsAsync(
        Guid playerProfileId,
        Guid groupChatId,
        CancellationToken cancellationToken = default) =>
        dbContext.PlayerGroupLinks.AnyAsync(
            x => x.PlayerProfileId == playerProfileId && x.GroupChatId == groupChatId,
            cancellationToken);

    public async Task<IReadOnlyList<PlayerGroupReadModel>> ListPlayerGroupsAsync(
        Guid playerProfileId,
        CancellationToken cancellationToken = default) =>
        await (
            from link in dbContext.PlayerGroupLinks
            join groupChat in dbContext.GroupChats on link.GroupChatId equals groupChat.Id
            where link.PlayerProfileId == playerProfileId
            orderby link.IsPrimary descending, groupChat.GroupName, groupChat.Id
            select new PlayerGroupReadModel(
                groupChat.Id,
                groupChat.ExternalId,
                groupChat.GroupName,
                groupChat.WhatsAppMemberCount,
                link.IsPrimary))
            .ToArrayAsync(cancellationToken);

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
