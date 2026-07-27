using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>
/// Commit boundary that wraps the <see cref="SouthBaySoccerDbContext"/> and persists all
/// pending changes as a single application operation.
/// </summary>
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly SouthBaySoccerDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWork"/> class.
    /// </summary>
    /// <param name="context">The EF Core context to commit.</param>
    public UnitOfWork(SouthBaySoccerDbContext context)
    {
        _context = context;
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
            // Cache eviction is drained by AuditSoftDeleteSaveChangesInterceptor.SavedChangesAsync,
            // so every commit path is covered, not just this one.
            return await _context.SaveChangesAsync(cancellationToken);
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
