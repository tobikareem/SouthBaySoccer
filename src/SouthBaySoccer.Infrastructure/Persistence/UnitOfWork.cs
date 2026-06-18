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
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
