using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Operations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for transactional outbox messages. Outbox rows are immutable operational records:
/// they are never soft-deleted and are only ever added or advanced through their status.
/// </summary>
public interface IOutboxMessageRepository
{
    /// <summary>Adds a new outbox message.</summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>Finds the message carrying an idempotency key, if one was already written.</summary>
    /// <param name="idempotencyKey">The unique idempotency key.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<OutboxMessage?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing outbox message as modified.</summary>
    /// <param name="message">The message to update.</param>
    void Update(OutboxMessage message);

    /// <summary>
    /// Atomically claims due messages of the given types for one processor run. A message is due
    /// when it is <c>Pending</c> or <c>RetryScheduled</c> with <c>AvailableAtUtc</c> at or before
    /// <paramref name="nowUtc"/>, or <c>Processing</c> with a lock that expired before
    /// <paramref name="nowUtc"/> (an earlier run died). Each claim is a conditional update, so two
    /// concurrent processors never receive the same row. Returned rows are tracked and carry
    /// <paramref name="lockToken"/> and <paramref name="lockedUntilUtc"/>.
    /// </summary>
    /// <param name="messageTypes">The message types this processor handles.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="lockToken">An opaque token identifying this processor run.</param>
    /// <param name="lockedUntilUtc">When the claim expires if the run does not finish.</param>
    /// <param name="batchSize">The maximum number of rows to claim.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(
        IReadOnlyCollection<string> messageTypes,
        DateTime nowUtc,
        string lockToken,
        DateTime lockedUntilUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}
