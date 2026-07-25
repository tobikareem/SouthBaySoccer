using System;
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
