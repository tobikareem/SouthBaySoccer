using SouthBaySoccer.Domain.Entities.Scheduling;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for imported Pickup Pal game snapshots and their participant rosters.
/// </summary>
public interface IPickupPalGameRepository
{
    /// <summary>Finds the snapshot for a Pickup Pal game id, or null when never imported.</summary>
    Task<PickupPalGameSnapshot?> FindSnapshotByGameIdAsync(
        string pickupPalGameId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new game snapshot.</summary>
    Task AddSnapshotAsync(PickupPalGameSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing snapshot as modified.</summary>
    void UpdateSnapshot(PickupPalGameSnapshot snapshot);

    /// <summary>
    /// Replaces the session's imported participant rows with the given list. Existing rows are
    /// soft-deleted; the replacement list is inserted as-is.
    /// </summary>
    Task ReplaceParticipantsAsync(
        Guid sessionId,
        IReadOnlyList<PickupPalGameParticipant> participants,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the session's imported participants ordered by join order.</summary>
    Task<IReadOnlyList<PickupPalGameParticipant>> ListParticipantsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a single imported participant row by its id, or null.</summary>
    Task<PickupPalGameParticipant?> FindParticipantAsync(
        Guid participantId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an existing participant row as modified (e.g. after linking it to a profile).</summary>
    void UpdateParticipant(PickupPalGameParticipant participant);
}
