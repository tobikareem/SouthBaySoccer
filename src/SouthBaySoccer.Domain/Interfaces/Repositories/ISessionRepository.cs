using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for pickup session scheduling.
/// </summary>
public interface ISessionRepository : IRepository<Session>
{
    /// <summary>
    /// Finds a session by its deterministic occurrence key.
    /// </summary>
    Task<Session?> FindByOccurrenceKeyAsync(string occurrenceKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists upcoming sessions with a bounded result count.
    /// </summary>
    Task<IReadOnlyList<Session>> ListUpcomingAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a recurrence rule by id.
    /// </summary>
    Task<RecurrenceRule?> FindRecurrenceRuleAsync(Guid recurrenceRuleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a recurrence rule.
    /// </summary>
    Task AddRecurrenceRuleAsync(RecurrenceRule recurrenceRule, CancellationToken cancellationToken = default);
}
