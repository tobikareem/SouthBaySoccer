using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for season management.
/// </summary>
public interface ISeasonRepository : IRepository<Season>
{
    /// <summary>
    /// Finds a season by its display name.
    /// </summary>
    Task<Season?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active seasons ordered by start date.
    /// </summary>
    Task<IReadOnlyList<Season>> ListActiveAsync(CancellationToken cancellationToken = default);
}
