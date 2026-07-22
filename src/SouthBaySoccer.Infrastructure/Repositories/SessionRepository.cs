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

    // Title equality relies on the database's default case-insensitive collation; canceled
    // sessions are excluded so a replacement for a canceled game is not treated as a duplicate.
    public Task<bool> ExistsDuplicateAsync(
        Guid venueId,
        string title,
        DateTime startsAtUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.Sessions.AnyAsync(
            x => x.VenueId == venueId
                && x.Title == title
                && x.StartsAtUtc == startsAtUtc
                && x.Status != SessionStatus.Canceled,
            cancellationToken);

    public async Task<IReadOnlyList<Session>> ListUpcomingAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Sessions
            .Where(x => x.StartsAtUtc >= fromUtc
                && (x.Status == SessionStatus.Published || x.Status == SessionStatus.Canceled))
            .OrderBy(x => x.StartsAtUtc)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> ListManagedAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Sessions
            .Where(x => x.StartsAtUtc >= fromUtc)
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
