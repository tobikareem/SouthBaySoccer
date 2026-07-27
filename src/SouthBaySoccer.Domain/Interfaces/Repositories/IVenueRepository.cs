using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for reusable venues.
/// </summary>
public interface IVenueRepository : IRepository<Venue>
{
    /// <summary>
    /// Lists active venues ordered by name.
    /// </summary>
    Task<IReadOnlyList<Venue>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the venues matching any of the given names in one query. Used by paths that decide
    /// whether to create a venue, which must never read a cached or truncated list.
    /// </summary>
    Task<IReadOnlyList<Venue>> ListByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the venues with the given ids in one query.
    /// </summary>
    Task<IReadOnlyList<Venue>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
