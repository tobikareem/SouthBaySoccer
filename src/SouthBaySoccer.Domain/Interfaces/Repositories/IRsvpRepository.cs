using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for RSVP, waitlist, check-in, and admin override workflows.
/// </summary>
public interface IRsvpRepository
{
    /// <summary>
    /// Submits a player's RSVP intent using a serializable capacity check.
    /// </summary>
    Task<RsvpMutationResult> SubmitRsvpAsync(
        Guid sessionId,
        Guid playerProfileId,
        RsvpStatus requestedStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the player's active RSVP or waitlist state and promotes the next eligible waitlist
    /// entry. <paramref name="checkPromotionEligibilityAsync"/> is invoked at most once with every
    /// active waitlist candidate and must return a verdict per candidate; candidates it omits are
    /// treated as ineligible. Evaluating the whole waitlist in one call keeps the compliance read
    /// inside the transaction — so an expiry is never written from a stale verdict — while costing
    /// one query instead of one per candidate.
    /// </summary>
    Task<RsvpMutationResult> CancelAndPromoteAsync(
        Guid sessionId,
        Guid playerProfileId,
        Func<IReadOnlyCollection<Guid>, CancellationToken, Task<IReadOnlyDictionary<Guid, bool>>> checkPromotionEligibilityAsync,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a player as Going with an admin override audit record.
    /// </summary>
    Task<RsvpMutationResult> AddWithAdminOverrideAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid adminPlayerProfileId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a check-in outcome for a confirmed player.
    /// </summary>
    Task<CheckInMutationResult> RecordCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid checkedInByPlayerProfileId,
        DateTime checkedInAtUtc,
        AttendanceOutcome outcome,
        string? lateOverrideReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records NoShow outcomes for confirmed players without an active check-in.
    /// </summary>
    Task<int> RecordNoShowsAsync(
        Guid sessionId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the player's current active RSVP state for a session.
    /// </summary>
    Task<RsvpMutationResult?> GetMyRsvpAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the session's confirmed (Going) players with their profile display data.
    /// </summary>
    Task<IReadOnlyList<RosterMemberRecord>> ListGoingRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists only players whose active attendance outcome is CheckedIn or Late.
    /// </summary>
    Task<IReadOnlyList<RosterMemberRecord>> ListCheckedInRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the session's active waitlist in position order with profile display data.
    /// </summary>
    Task<IReadOnlyList<RosterMemberRecord>> ListActiveWaitlistRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the compact attendance projection used by the Game Day screen.</summary>
    Task<GameDayAttendanceRecord> GetGameDayAttendanceAsync(
        Guid sessionId,
        Guid currentPlayerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the game-day attendance projection for several sessions in two batched queries.
    /// Every requested session id is present in the result, including sessions with no attendance.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, GameDayAttendanceRecord>> GetGameDayAttendanceBatchAsync(
        IReadOnlyCollection<Guid> sessionIds,
        Guid currentPlayerProfileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents one roster row backed by a local player profile.
/// </summary>
public sealed record RosterMemberRecord(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    int? WaitlistPosition);

/// <summary>Server-side attendance and eligibility facts for one session.</summary>
public sealed record GameDayAttendanceRecord(
    int GoingCount,
    int CheckedInCount,
    int LateCount,
    bool IsCurrentPlayerGoing,
    bool IsCurrentPlayerWaitlisted,
    bool IsCurrentPlayerCheckedIn,
    IReadOnlyList<Guid> CheckedInPlayerProfileIds);

/// <summary>
/// Represents the result of mutating or reading RSVP state.
/// </summary>
public sealed record RsvpMutationResult(
    Guid SessionId,
    Guid PlayerProfileId,
    RsvpMutationState State,
    Guid? RsvpResponseId = null,
    Guid? WaitlistEntryId = null,
    int? WaitlistPosition = null,
    Guid? PromotedPlayerProfileId = null);

/// <summary>
/// Represents a check-in write and optional admin override audit row.
/// </summary>
public sealed record CheckInMutationResult(
    CheckIn CheckIn,
    Guid? AdminOverrideId = null,
    string? LateOverrideReason = null);

/// <summary>
/// Identifies the effective RSVP state after a mutation.
/// </summary>
public enum RsvpMutationState
{
    /// <summary>The player has a confirmed spot.</summary>
    Going,
    /// <summary>The player may attend but does not consume capacity.</summary>
    Maybe,
    /// <summary>The player is not attending.</summary>
    NotGoing,
    /// <summary>The player is on the ordered waitlist.</summary>
    Waitlisted,
    /// <summary>The player no longer has an active RSVP or waitlist entry.</summary>
    Canceled
}
