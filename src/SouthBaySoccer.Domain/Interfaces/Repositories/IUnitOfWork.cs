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
}
