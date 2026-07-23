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
    /// Lists the bounded player-facing session feed with venue, Going, waitlist, and caller state.
    /// Local RSVP rows and imported Pickup Pal participants are de-duplicated by linked profile.
    /// </summary>
    Task<IReadOnlyList<SessionFeedRecord>> ListUpcomingFeedAsync(
        DateTime fromUtc,
        int take,
        Guid currentPlayerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists sessions that can be managed by an organizer with a bounded result count.
    /// </summary>
    Task<IReadOnlyList<Session>> ListManagedAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Lists published sessions starting within one venue-local calendar-day UTC range.</summary>
    Task<IReadOnlyList<Session>> ListGameDayCandidatesAsync(
        DateTime dayStartsAtUtc,
        DateTime dayEndsAtUtc,
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

/// <summary>Authoritative persisted facts for one card in the player-facing Sessions feed.</summary>
public sealed record SessionFeedRecord(
    Session Session,
    string VenueName,
    int GoingCount,
    int WaitlistCount,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerWaitlisted);
