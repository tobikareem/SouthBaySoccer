using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class SessionRepository(SouthBaySoccerDbContext dbContext) : ISessionRepository
{
    public Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Sessions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Session?> FindByOccurrenceKeyAsync(string occurrenceKey, CancellationToken cancellationToken = default) =>
        dbContext.Sessions.SingleOrDefaultAsync(x => x.OccurrenceKey == occurrenceKey, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListUpcomingAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Sessions
            .Where(x => x.StartsAtUtc >= fromUtc && x.Status != SessionStatus.Canceled)
            .OrderBy(x => x.StartsAtUtc)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public Task<RecurrenceRule?> FindRecurrenceRuleAsync(Guid recurrenceRuleId, CancellationToken cancellationToken = default) =>
        dbContext.RecurrenceRules.SingleOrDefaultAsync(x => x.Id == recurrenceRuleId, cancellationToken);

    public async Task AddAsync(Session entity, CancellationToken cancellationToken = default) =>
        await dbContext.Sessions.AddAsync(entity, cancellationToken);

    public async Task AddRecurrenceRuleAsync(RecurrenceRule recurrenceRule, CancellationToken cancellationToken = default) =>
        await dbContext.RecurrenceRules.AddAsync(recurrenceRule, cancellationToken);

    public void Update(Session entity) => dbContext.Sessions.Update(entity);

    public void SoftDelete(Session entity)
    {
        entity.IsDeleted = true;
        dbContext.Sessions.Update(entity);
    }
}
