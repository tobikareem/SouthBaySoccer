using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Caching;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>
/// Commit boundary that wraps the <see cref="SouthBaySoccerDbContext"/> and persists all
/// pending changes as a single application operation.
/// </summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly SouthBaySoccerDbContext _context;
    private readonly CacheEvictionQueue _cacheEvictionQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="context">The EF Core context to commit.</param>
    /// <param name="cacheEvictionQueue">Cache keys to evict once the commit succeeds.</param>
    public UnitOfWork(SouthBaySoccerDbContext context, CacheEvictionQueue cacheEvictionQueue)
    {
        _context = context;
        _cacheEvictionQueue = cacheEvictionQueue;
    }

    /// <summary>
    /// Persists all pending changes to the database.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var written = await _context.SaveChangesAsync(cancellationToken);
            // Only after the write is durable: evicting earlier lets a concurrent read repopulate
            // the entry from pre-commit state and pin it for a whole TTL.
            _cacheEvictionQueue.Flush();
            return written;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConflictException("The resource changed while this request was being saved. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException sqlException
            && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627))
        {
            throw new ApplicationConflictException("The requested change conflicts with a concurrent update. Refresh and try again.");
        }
    }
}
