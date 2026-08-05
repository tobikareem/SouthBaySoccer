using System.Threading;
using System.Threading.Tasks;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Defines the commit boundary for an application operation. All repository changes
/// made within a single operation are persisted together when
/// <see cref="SaveChangesAsync(CancellationToken)"/> is called.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes as a single unit of work.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of state entries written to the underlying store.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a read-check-write operation inside one serializable transaction and commits it,
    /// retrying on deadlock/serialization failures. Used by mutations whose guards read state that
    /// a concurrent request could change between the read and the write (draft picks racing for
    /// the same turn, auto-balance racing a lock). The operation must be re-runnable: it is retried
    /// from the top with a cleared change tracker, and it must not call
    /// <see cref="SaveChangesAsync"/> itself — the wrapper saves and commits.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The re-runnable read-check-write operation.</param>
    /// <param name="conflictMessage">User-facing message when retries are exhausted.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<T> ExecuteInSerializableTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string conflictMessage,
        CancellationToken cancellationToken = default);
}
