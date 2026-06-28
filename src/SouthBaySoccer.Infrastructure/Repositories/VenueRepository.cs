using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class VenueRepository(SouthBaySoccerDbContext dbContext) : IVenueRepository
{
    public Task<Venue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Venues.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Venue>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Venues.OrderBy(x => x.Name).Take(100).ToArrayAsync(cancellationToken);

    public async Task AddAsync(Venue entity, CancellationToken cancellationToken = default) =>
        await dbContext.Venues.AddAsync(entity, cancellationToken);

    public void Update(Venue entity) => dbContext.Venues.Update(entity);

    public void SoftDelete(Venue entity)
    {
        entity.IsDeleted = true;
        dbContext.Venues.Update(entity);
    }
}
