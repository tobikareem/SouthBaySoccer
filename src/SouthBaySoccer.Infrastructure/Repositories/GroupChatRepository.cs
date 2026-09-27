using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class GroupChatRepository(SouthBaySoccerDbContext dbContext) : IGroupChatRepository
{
    public Task<GroupChat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.GroupChats.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<GroupChat?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default) =>
        dbContext.GroupChats.SingleOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<GroupChat>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idArray = ids as Guid[] ?? ids.ToArray();
        return await dbContext.GroupChats
            .Where(x => idArray.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GroupChat>> ListByExternalIdsAsync(
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken = default)
    {
        if (externalIds.Count == 0)
        {
            return [];
        }

        var idArray = externalIds as string[] ?? externalIds.ToArray();
        return await dbContext.GroupChats
            .Where(x => idArray.Contains(x.ExternalId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GroupChat>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.GroupChats
            .OrderBy(x => x.GroupName)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(GroupChat entity, CancellationToken cancellationToken = default) =>
        await dbContext.GroupChats.AddAsync(entity, cancellationToken);

    public void Update(GroupChat entity) =>
        dbContext.GroupChats.Update(entity);

    public void SoftDelete(GroupChat entity)
    {
        entity.IsDeleted = true;
        dbContext.GroupChats.Update(entity);
    }
}
