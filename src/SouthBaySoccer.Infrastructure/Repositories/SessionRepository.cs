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

    public async Task<IReadOnlyList<Session>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var idArray = ids as Guid[] ?? ids.ToArray();
        return await dbContext.Sessions
            .Where(x => idArray.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> ListByOccurrenceKeysAsync(
        IReadOnlyCollection<string> occurrenceKeys,
        CancellationToken cancellationToken = default)
    {
        if (occurrenceKeys.Count == 0)
        {
            return [];
        }

        // Ordered so that an occurrence key held by more than one session resolves deterministically
        // to the oldest; the single-key lookup used SingleOrDefault and simply threw on duplicates.
        var keyArray = occurrenceKeys as string[] ?? occurrenceKeys.ToArray();
        return await dbContext.Sessions
            .Where(x => x.OccurrenceKey != null && keyArray.Contains(x.OccurrenceKey))
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyList<SessionFeedRecord>> ListUpcomingFeedAsync(
        DateTime fromUtc,
        int take,
        Guid currentPlayerProfileId,
        CancellationToken cancellationToken = default)
    {
        var sessionRows = await (
            from session in dbContext.Sessions
            join venue in dbContext.Venues on session.VenueId equals venue.Id
            where session.StartsAtUtc >= fromUtc
                && (session.Status == SessionStatus.Published || session.Status == SessionStatus.Canceled)
            orderby session.StartsAtUtc, session.Id
            select new { Session = session, VenueName = venue.Name })
            .Take(take)
            .ToArrayAsync(cancellationToken);
        if (sessionRows.Length == 0)
        {
            return [];
        }

        var sessionIds = sessionRows.Select(x => x.Session.Id).ToArray();
        var localGoing = await dbContext.RsvpResponses
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == RsvpStatus.Going)
            .Select(x => new { x.SessionId, x.PlayerProfileId })
            .ToArrayAsync(cancellationToken);
        var localWaitlist = await dbContext.WaitlistEntries
            .Where(x => sessionIds.Contains(x.SessionId) && x.Status == WaitlistEntryStatus.Active)
            .Select(x => new { x.SessionId, x.PlayerProfileId })
            .ToArrayAsync(cancellationToken);
        var imported = await dbContext.Set<PickupPalGameParticipant>()
            .Where(x => sessionIds.Contains(x.SessionId))
            .Select(x => new
            {
                x.SessionId,
                x.PlayerProfileId,
                x.PickupPalParticipantId,
                x.IsWaitlist,
            })
            .ToArrayAsync(cancellationToken);
        // One snapshot per session today (the occurrence key is "pickuppal:{gameId}"), but group
        // instead of Single so a future many-to-one import shape degrades gracefully. The ordered
        // First keeps the pick deterministic — the query returns rows in no guaranteed order.
        var groupNamesBySession = (await dbContext.Set<PickupPalGameSnapshot>()
            .Where(x => sessionIds.Contains(x.SessionId) && x.GroupName != string.Empty)
            .Select(x => new { x.SessionId, x.GroupName })
            .ToArrayAsync(cancellationToken))
            .GroupBy(x => x.SessionId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(x => x.GroupName, StringComparer.Ordinal).First().GroupName);

        var attendanceBySession = SessionAttendanceProjection.Build(
            localGoing
                .Select(x => new SessionAttendanceEntry(
                    x.SessionId,
                    SessionAttendanceProjection.ProfileKey(x.PlayerProfileId),
                    SessionAttendanceState.Going,
                    SessionAttendanceSource.Local))
                .Concat(localWaitlist.Select(x => new SessionAttendanceEntry(
                    x.SessionId,
                    SessionAttendanceProjection.ProfileKey(x.PlayerProfileId),
                    SessionAttendanceState.Waitlisted,
                    SessionAttendanceSource.Local)))
                .Concat(imported.Select(x => new SessionAttendanceEntry(
                    x.SessionId,
                    SessionAttendanceProjection.ParticipantKey(
                        x.PlayerProfileId,
                        x.PickupPalParticipantId),
                    x.IsWaitlist
                        ? SessionAttendanceState.Waitlisted
                        : SessionAttendanceState.Going,
                    SessionAttendanceSource.Imported))));

        return sessionRows.Select(row =>
        {
            var attendance = attendanceBySession.TryGetValue(row.Session.Id, out var snapshot)
                ? snapshot
                : SessionAttendanceProjection.BuildForSession(
                    row.Session.Id,
                    Array.Empty<SessionAttendanceEntry>());
            var currentPlayerKey = SessionAttendanceProjection.ProfileKey(currentPlayerProfileId);

            return new SessionFeedRecord(
                row.Session,
                row.VenueName,
                attendance.GoingKeys.Count,
                attendance.WaitlistKeys.Count,
                attendance.GoingKeys.Contains(currentPlayerKey),
                attendance.WaitlistKeys.Contains(currentPlayerKey),
                groupNamesBySession.GetValueOrDefault(row.Session.Id));
        }).ToArray();
    }

    public async Task<IReadOnlyList<Session>> ListManagedAsync(
        DateTime fromUtc,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.Sessions
            .Where(x => x.StartsAtUtc >= fromUtc)
            .OrderBy(x => x.StartsAtUtc)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Session>> ListGameDayCandidatesAsync(
        DateTime dayStartsAtUtc,
        DateTime dayEndsAtUtc,
        CancellationToken cancellationToken = default) =>
        await dbContext.Sessions
            .Where(x => x.Status == SessionStatus.Published
                && x.StartsAtUtc >= dayStartsAtUtc
                && x.StartsAtUtc < dayEndsAtUtc)
            .OrderBy(x => x.StartsAtUtc)
            .ThenBy(x => x.Id)
            .Take(10)
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
