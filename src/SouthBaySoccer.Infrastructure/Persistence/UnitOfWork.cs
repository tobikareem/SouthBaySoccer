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

    /// <inheritdoc />
    // Same shape as RsvpRepository.ExecuteInSerializableTransactionAsync (the in-repo precedent for
    // read-check-write races): serializable isolation makes the guards' reads range-locked, the
    // execution strategy wraps the manual transaction (required by EnableRetryOnFailure), and
    // deadlock/serialization victims retry from the top with a cleared change tracker.
    public async Task<T> ExecuteInSerializableTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string conflictMessage,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        cancellationToken);
                    var result = await operation(cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                });
            }
            catch (Exception ex) when (IsRetryableConcurrencyFailure(ex) && attempt < maxAttempts)
            {
                _context.ChangeTracker.Clear();
            }
            catch (Exception ex) when (IsRetryableConcurrencyFailure(ex))
            {
                throw new ApplicationConflictException(conflictMessage);
            }
        }

        throw new ApplicationConflictException(conflictMessage);
    }

    private static bool IsRetryableConcurrencyFailure(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: SqlException sqlException } && IsRetryableSqlFailure(sqlException)
        || exception is SqlException directSqlException && IsRetryableSqlFailure(directSqlException);

    private static bool IsRetryableSqlFailure(SqlException exception) =>
        exception.Errors.Cast<SqlError>().Any(error => error.Number is 1205 or 2601 or 2627 or 3960);
}
