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
    /// Lists the sessions with the given ids in one query.
    /// </summary>
    Task<IReadOnlyList<Session>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the sessions carrying the given occurrence keys in one query.
    /// </summary>
    Task<IReadOnlyList<Session>> ListByOccurrenceKeysAsync(
        IReadOnlyCollection<string> occurrenceKeys,
        CancellationToken cancellationToken = default);

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
    /// Lists published sessions that started within a past UTC window, newest first, capped at
    /// <paramref name="take"/>. Used to find a player's most recent game.
    /// </summary>
    Task<IReadOnlyList<Session>> ListPastGameDayCandidatesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps sessions to the WhatsApp group name recorded on their Pickup Pal snapshot. Sessions
    /// without a snapshot (created by hand) are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetGroupNamesBySessionAsync(
        IReadOnlyCollection<Guid> sessionIds,
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
/// <param name="GroupName">
/// The WhatsApp group chat name the session was imported from, or <see langword="null"/> for a
/// session an organizer created by hand (those carry no Pickup Pal group).
/// </param>
public sealed record SessionFeedRecord(
    Session Session,
    string VenueName,
    int GoingCount,
    int WaitlistCount,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerWaitlisted,
    string? GroupName = null);
