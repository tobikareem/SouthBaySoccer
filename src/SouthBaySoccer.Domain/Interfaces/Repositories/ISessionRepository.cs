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
    /// Determines whether an active (non-canceled) session already exists at the same venue with
    /// the same title and the same start time.
    /// </summary>
    Task<bool> ExistsDuplicateAsync(
        Guid venueId,
        string title,
        DateTime startsAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists upcoming sessions with a bounded result count.
    /// </summary>
    Task<IReadOnlyList<Session>> ListUpcomingAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists sessions that can be managed by an organizer with a bounded result count.
    /// </summary>
    Task<IReadOnlyList<Session>> ListManagedAsync(
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
