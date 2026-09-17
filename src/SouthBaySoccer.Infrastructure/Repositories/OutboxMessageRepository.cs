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
        // carrying this run's token is ours. The type filter must still apply: a lock token is only
        // guaranteed unique per claim call within a single caller, so without it this could return
        // a message of a type this caller never asked to claim (or, in tests, a stale row left
        // "Processing" by an earlier claim that reused the same literal token).
        //
        // AsNoTracking is required here for correctness, not just performance: ExecuteUpdate never
        // touches the change tracker, so if this context already has any of these rows tracked
        // from an earlier query in the same scope, a tracked re-read would perform identity
        // resolution and hand back those STALE in-memory instances instead of the values just
        // written to the database. Callers get a fresh, correct snapshot; OutboxProcessor already
        // settles each claimed message through its own separate scope, so returning detached
        // entities here does not affect it.
        return await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => x.LockToken == lockToken
                && x.Status == OutboxMessageStatus.Processing
                && types.Contains(x.MessageType))
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
