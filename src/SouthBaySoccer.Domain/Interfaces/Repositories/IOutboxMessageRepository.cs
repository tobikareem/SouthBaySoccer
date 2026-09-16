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
}
