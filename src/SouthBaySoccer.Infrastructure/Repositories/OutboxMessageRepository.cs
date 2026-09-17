using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
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

    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(
        IReadOnlyCollection<string> messageTypes,
        DateTime nowUtc,
        string lockToken,
        DateTime lockedUntilUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (messageTypes.Count == 0 || batchSize <= 0)
        {
            return [];
        }

        var types = messageTypes.ToArray();
        var candidateIds = await DueMessages(types, nowUtc)
            .OrderBy(x => x.AvailableAtUtc)
            .ThenBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);
        if (candidateIds.Length == 0)
        {
            return [];
        }

        // One conditional UPDATE per candidate: the WHERE re-checks "still due and unlocked", so
        // when two instances race for the same row exactly one sees an affected row count of 1.
        // Per-row statements keep this on the well-supported ExecuteUpdate path (no TOP/Take).
        foreach (var id in candidateIds)
        {
            await DueMessages(types, nowUtc)
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, OutboxMessageStatus.Processing)
                        .SetProperty(x => x.LockToken, lockToken)
                        .SetProperty(x => x.LockedUntilUtc, lockedUntilUtc)
                        .SetProperty(x => x.UpdatedAt, nowUtc),
                    cancellationToken);
        }

        // Re-read by lock token alone rather than trusting local copies: ExecuteUpdate bypasses the
        // change tracker, and under EnableRetryOnFailure an ambiguous UPDATE can commit and then
        // re-run reporting zero rows, so a claimed row may be missing from claimedIds. Every row
        // carrying this run's token is ours.
        return await dbContext.OutboxMessages
            .Where(x => x.LockToken == lockToken && x.Status == OutboxMessageStatus.Processing)
            .OrderBy(x => x.AvailableAtUtc)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<OutboxMessage> DueMessages(string[] messageTypes, DateTime nowUtc) =>
        dbContext.OutboxMessages.Where(x =>
            messageTypes.Contains(x.MessageType)
            && (((x.Status == OutboxMessageStatus.Pending || x.Status == OutboxMessageStatus.RetryScheduled)
                    && x.AvailableAtUtc <= nowUtc
                    && (x.LockedUntilUtc == null || x.LockedUntilUtc < nowUtc))
                || (x.Status == OutboxMessageStatus.Processing
                    && x.LockedUntilUtc != null
                    && x.LockedUntilUtc < nowUtc)));
}
