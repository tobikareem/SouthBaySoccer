using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PickupPalGameRepository(SouthBaySoccerDbContext dbContext) : IPickupPalGameRepository
{
    public Task<PickupPalGameSnapshot?> FindSnapshotByGameIdAsync(
        string pickupPalGameId,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<PickupPalGameSnapshot>()
            .SingleOrDefaultAsync(x => x.PickupPalGameId == pickupPalGameId, cancellationToken);

    public async Task AddSnapshotAsync(
        PickupPalGameSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<PickupPalGameSnapshot>().AddAsync(snapshot, cancellationToken);

    public void UpdateSnapshot(PickupPalGameSnapshot snapshot) =>
        dbContext.Set<PickupPalGameSnapshot>().Update(snapshot);

    public async Task ReplaceParticipantsAsync(
        Guid sessionId,
        IReadOnlyList<PickupPalGameParticipant> participants,
        CancellationToken cancellationToken = default)
    {
        // Diff-and-upsert keyed on the Pickup Pal participant id so re-imports keep stable row ids
        // instead of soft-deleting and re-inserting the whole roster on every pass.
        var existing = await dbContext.Set<PickupPalGameParticipant>()
            .Where(x => x.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        var existingByParticipantId = existing.ToDictionary(x => x.PickupPalParticipantId);
        var incomingParticipantIds = new HashSet<string>();

        foreach (var participant in participants)
        {
            incomingParticipantIds.Add(participant.PickupPalParticipantId);
            if (existingByParticipantId.TryGetValue(participant.PickupPalParticipantId, out var row))
            {
                row.DisplayName = participant.DisplayName;
                row.IsGuest = participant.IsGuest;
                row.IsWaitlist = participant.IsWaitlist;
                row.DisplayOrder = participant.DisplayOrder;
                row.JoinedAtUtc = participant.JoinedAtUtc;
            }
            else
            {
                await dbContext.Set<PickupPalGameParticipant>().AddAsync(participant, cancellationToken);
            }
        }

        foreach (var row in existing)
        {
            if (!incomingParticipantIds.Contains(row.PickupPalParticipantId))
            {
                row.IsDeleted = true;
            }
        }
    }

    public async Task<IReadOnlyList<PickupPalGameParticipant>> ListParticipantsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<PickupPalGameParticipant>()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.DisplayOrder)
            .ToArrayAsync(cancellationToken);
}
