using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class OutboxMessageRepository(SouthBaySoccerDbContext dbContext) : IOutboxMessageRepository
{
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);

    public Task<OutboxMessage?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        dbContext.OutboxMessages.SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Update(OutboxMessage message) => dbContext.OutboxMessages.Update(message);
}
