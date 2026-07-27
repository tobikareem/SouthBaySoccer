using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class VenueRepository(SouthBaySoccerDbContext dbContext) : IVenueRepository
{
    public Task<Venue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Venues.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    // AsNoTracking: this list is cached process-wide, so it must never hand out entities still
    // attached to a request's DbContext - a later Update or SoftDelete on one would mutate shared
    // state and re-attach an instance from a disposed context.
    public async Task<IReadOnlyList<Venue>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Venues.AsNoTracking().OrderBy(x => x.Name).Take(100).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Venue>> ListByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var nameArray = names as string[] ?? names.ToArray();
        return await dbContext.Venues
            .Where(x => nameArray.Contains(x.Name))
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Venue>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idArray = ids as Guid[] ?? ids.ToArray();
        return await dbContext.Venues
            .Where(x => idArray.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAsync(Venue entity, CancellationToken cancellationToken = default) =>
        await dbContext.Venues.AddAsync(entity, cancellationToken);

    public void Update(Venue entity) => dbContext.Venues.Update(entity);

    public void SoftDelete(Venue entity)
    {
        entity.IsDeleted = true;
        dbContext.Venues.Update(entity);
    }
}
