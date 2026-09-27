using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class SeasonRepository(SouthBaySoccerDbContext dbContext) : ISeasonRepository
{
    public Task<Season?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Seasons.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Season?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Seasons.SingleOrDefaultAsync(x => x.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Season>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Seasons.AsNoTracking().OrderByDescending(x => x.StartsAtUtc).Take(100).ToArrayAsync(cancellationToken);

    public async Task AddAsync(Season entity, CancellationToken cancellationToken = default) =>
        await dbContext.Seasons.AddAsync(entity, cancellationToken);

    public void Update(Season entity) => dbContext.Seasons.Update(entity);

    public void SoftDelete(Season entity)
    {
        entity.IsDeleted = true;
        dbContext.Seasons.Update(entity);
    }
}
