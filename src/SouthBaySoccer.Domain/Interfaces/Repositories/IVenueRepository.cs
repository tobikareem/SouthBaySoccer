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
}
