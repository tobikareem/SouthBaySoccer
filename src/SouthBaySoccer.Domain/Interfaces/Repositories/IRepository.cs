using System;
using System.Threading;
using System.Threading.Tasks;
using SouthBaySoccer.Domain.Entities.Common;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Generic repository abstraction for aggregate persistence. Constrained to
/// <see cref="BaseEntity"/> so every entity carries identity, audit, and soft-delete fields.
/// Deletion is always soft; there is no hard-delete operation.
/// </summary>
/// <typeparam name="T">The entity type, which must inherit <see cref="BaseEntity"/>.</typeparam>
public interface IRepository<T>
    where T : BaseEntity
{
    /// <summary>
    /// Retrieves a single entity by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the entity.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching entity, or <see langword="null"/> if none exists.</returns>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Note: no unbounded GetAllAsync() on the base interface — it cannot honor the
    // "filter and paginate large tables" rule (CLAUDE.md / architecture §13). Aggregate-
    // specific repositories expose paginated or predicate-based reads instead.

    /// <summary>
    /// Adds a new entity to the repository.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an existing entity as modified.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    void Update(T entity);

    /// <summary>
    /// Soft-deletes an entity by setting its <see cref="BaseEntity.IsDeleted"/> flag.
    /// The record is never physically removed.
    /// </summary>
    /// <param name="entity">The entity to soft-delete.</param>
    void SoftDelete(T entity);
}
