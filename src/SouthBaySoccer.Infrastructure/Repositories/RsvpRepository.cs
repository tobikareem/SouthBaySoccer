using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class RsvpRepository(SouthBaySoccerDbContext dbContext, IClock clock) : IRsvpRepository
{
    public Task<RsvpMutationResult> SubmitRsvpAsync(
        Guid sessionId,
        Guid playerProfileId,
        RsvpStatus requestedStatus,
        CancellationToken cancellationToken = default) =>
        ExecuteInSerializableTransactionAsync(async token =>
        {
            var session = await GetSessionAsync(sessionId, token);
            var rsvp = await FindRsvpAsync(sessionId, playerProfileId, token);
            var waitlistEntry = await FindActiveWaitlistEntryAsync(sessionId, playerProfileId, token);

            if (requestedStatus is RsvpStatus.Maybe or RsvpStatus.NotGoing)
            {
                rsvp = await UpsertRsvpAsync(rsvp, sessionId, playerProfileId, requestedStatus, token);
                CancelWaitlist(waitlistEntry);

                var state = requestedStatus == RsvpStatus.Maybe ? RsvpMutationState.Maybe : RsvpMutationState.NotGoing;
                return new RsvpMutationResult(sessionId, playerProfileId, state, rsvp.Id);
            }

            var confirmedCount = await dbContext.RsvpResponses
                .CountAsync(x => x.SessionId == sessionId && x.Status == RsvpStatus.Going, token);

            if (rsvp?.Status == RsvpStatus.Going || confirmedCount < session.Capacity)
            {
                rsvp = await UpsertRsvpAsync(rsvp, sessionId, playerProfileId, RsvpStatus.Going, token);
                CancelWaitlist(waitlistEntry);
                return new RsvpMutationResult(sessionId, playerProfileId, RsvpMutationState.Going, rsvp.Id);
            }

            waitlistEntry ??= await AddWaitlistEntryAsync(sessionId, playerProfileId, token);
            return new RsvpMutationResult(
                sessionId,
                playerProfileId,
                RsvpMutationState.Waitlisted,
                rsvp?.Id,
                waitlistEntry.Id,
                waitlistEntry.Position);
        }, cancellationToken);

    public Task<RsvpMutationResult> CancelAndPromoteAsync(
        Guid sessionId,
        Guid playerProfileId,
        Func<Guid, CancellationToken, Task<bool>> isEligibleForPromotion,
        CancellationToken cancellationToken = default) =>
        ExecuteInSerializableTransactionAsync(async token =>
        {
            _ = await GetSessionAsync(sessionId, token);

            var rsvp = await FindRsvpAsync(sessionId, playerProfileId, token);
            var releasedConfirmedSpot = rsvp?.Status == RsvpStatus.Going;
            if (rsvp is not null)
            {
                rsvp.IsDeleted = true;
            }

            CancelWaitlist(await FindActiveWaitlistEntryAsync(sessionId, playerProfileId, token));
            var promotedEntry = releasedConfirmedSpot
                ? await PromoteNextEligibleAsync(sessionId, isEligibleForPromotion, token)
                : null;

            return new RsvpMutationResult(
                sessionId,
                playerProfileId,
                RsvpMutationState.Canceled,
                rsvp?.Id,
                PromotedPlayerProfileId: promotedEntry?.PlayerProfileId);
        }, cancellationToken);

    public Task<RsvpMutationResult> AddWithAdminOverrideAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid adminPlayerProfileId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ExecuteInSerializableTransactionAsync(async token =>
        {
            _ = await GetSessionAsync(sessionId, token);
            var rsvp = await UpsertRsvpAsync(
                await FindRsvpAsync(sessionId, playerProfileId, token),
                sessionId,
                playerProfileId,
                RsvpStatus.Going,
                token);

            CancelWaitlist(await FindActiveWaitlistEntryAsync(sessionId, playerProfileId, token));

            await dbContext.AdminOverrides.AddAsync(
                new AdminOverride
                {
                    SessionId = sessionId,
                    PlayerProfileId = playerProfileId,
                    AdminPlayerProfileId = adminPlayerProfileId,
                    OverrideType = AdminOverrideType.Waiver,
                    Reason = reason,
                    AppliedAtUtc = clock.UtcNow
                },
                token);

            return new RsvpMutationResult(sessionId, playerProfileId, RsvpMutationState.Going, rsvp.Id);
        }, cancellationToken);

    public Task<CheckInMutationResult> RecordCheckInAsync(
        Guid sessionId,
        Guid playerProfileId,
        Guid checkedInByPlayerProfileId,
        DateTime checkedInAtUtc,
        AttendanceOutcome outcome,
        string? lateOverrideReason = null,
        CancellationToken cancellationToken = default) =>
        ExecuteInSerializableTransactionAsync(async token =>
        {
            var hasConfirmedRsvp = await dbContext.RsvpResponses
                .AnyAsync(x => x.SessionId == sessionId && x.PlayerProfileId == playerProfileId && x.Status == RsvpStatus.Going, token);
            if (!hasConfirmedRsvp)
            {
                throw new InvalidOperationException("Only confirmed players can be checked in.");
            }

            var checkIn = await dbContext.CheckIns
                .SingleOrDefaultAsync(x => x.SessionId == sessionId && x.PlayerProfileId == playerProfileId, token);

            if (checkIn is null)
            {
                checkIn = new CheckIn
                {
                    SessionId = sessionId,
                    PlayerProfileId = playerProfileId
                };
                await dbContext.CheckIns.AddAsync(checkIn, token);
            }

            checkIn.CheckedInByPlayerProfileId = checkedInByPlayerProfileId;
            checkIn.CheckedInAtUtc = checkedInAtUtc;
            checkIn.Outcome = outcome;

            if (string.IsNullOrWhiteSpace(lateOverrideReason))
            {
                return new CheckInMutationResult(checkIn);
            }

            var adminOverride = new AdminOverride
            {
                SessionId = sessionId,
                PlayerProfileId = playerProfileId,
                AdminPlayerProfileId = checkedInByPlayerProfileId,
                OverrideType = AdminOverrideType.CheckIn,
                Reason = lateOverrideReason.Trim(),
                AppliedAtUtc = checkedInAtUtc
            };
            await dbContext.AdminOverrides.AddAsync(adminOverride, token);
            return new CheckInMutationResult(checkIn, adminOverride.Id, adminOverride.Reason);
        }, cancellationToken);

    public Task<int> RecordNoShowsAsync(
        Guid sessionId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default) =>
        ExecuteInSerializableTransactionAsync(async token =>
        {
            var confirmedWithoutCheckIn = await dbContext.RsvpResponses
                .Where(x => x.SessionId == sessionId && x.Status == RsvpStatus.Going)
                .Where(x => !dbContext.CheckIns.Any(c => c.SessionId == sessionId && c.PlayerProfileId == x.PlayerProfileId))
                .Select(x => x.PlayerProfileId)
                .ToArrayAsync(token);

            foreach (var playerProfileId in confirmedWithoutCheckIn)
            {
                await dbContext.CheckIns.AddAsync(
                    new CheckIn
                    {
                        SessionId = sessionId,
                        PlayerProfileId = playerProfileId,
                        CheckedInAtUtc = occurredAtUtc,
                        Outcome = AttendanceOutcome.NoShow
                    },
                    token);
            }

            return confirmedWithoutCheckIn.Length;
        }, cancellationToken);

    public async Task<RsvpMutationResult?> GetMyRsvpAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        var waitlistEntry = await FindActiveWaitlistEntryAsync(sessionId, playerProfileId, cancellationToken);
        if (waitlistEntry is not null)
        {
            return new RsvpMutationResult(
                sessionId,
                playerProfileId,
                RsvpMutationState.Waitlisted,
                WaitlistEntryId: waitlistEntry.Id,
                WaitlistPosition: waitlistEntry.Position);
        }

        var rsvp = await FindRsvpAsync(sessionId, playerProfileId, cancellationToken);
        if (rsvp is null)
        {
            return null;
        }

        var state = rsvp.Status switch
        {
            RsvpStatus.Going => RsvpMutationState.Going,
            RsvpStatus.Maybe => RsvpMutationState.Maybe,
            RsvpStatus.NotGoing => RsvpMutationState.NotGoing,
            _ => throw new InvalidOperationException("Unsupported RSVP status.")
        };

        return new RsvpMutationResult(sessionId, playerProfileId, state, rsvp.Id);
    }

    public async Task<IReadOnlyList<RosterMemberRecord>> ListGoingRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await dbContext.RsvpResponses
            .Where(x => x.SessionId == sessionId && x.Status == RsvpStatus.Going)
            .Join(
                dbContext.PlayerProfiles,
                rsvp => rsvp.PlayerProfileId,
                profile => profile.Id,
                (rsvp, profile) => new RosterMemberRecord(
                    profile.Id,
                    profile.DisplayName,
                    profile.PreferredPosition,
                    profile.IsGuest,
                    null))
            .OrderBy(x => x.DisplayName)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<RosterMemberRecord>> ListActiveWaitlistRosterAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        await dbContext.WaitlistEntries
            .Where(x => x.SessionId == sessionId && x.Status == WaitlistEntryStatus.Active)
            .Join(
                dbContext.PlayerProfiles,
                entry => entry.PlayerProfileId,
                profile => profile.Id,
                (entry, profile) => new RosterMemberRecord(
                    profile.Id,
                    profile.DisplayName,
                    profile.PreferredPosition,
                    profile.IsGuest,
                    entry.Position))
            .OrderBy(x => x.WaitlistPosition)
            .ToArrayAsync(cancellationToken);

    private async Task<T> ExecuteInSerializableTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var strategy = dbContext.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                    var result = await operation(cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                });
            }
            catch (Exception ex) when (IsRetryableConcurrencyFailure(ex) && attempt < maxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception ex) when (IsRetryableConcurrencyFailure(ex))
            {
                throw new ApplicationConflictException("The RSVP roster changed while processing this request. Try again.");
            }
        }

        throw new ApplicationConflictException("The RSVP roster changed while processing this request. Try again.");
    }

    private static bool IsRetryableConcurrencyFailure(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: SqlException sqlException } && IsRetryableSqlFailure(sqlException)
        || exception is SqlException directSqlException && IsRetryableSqlFailure(directSqlException);

    private static bool IsRetryableSqlFailure(SqlException exception) =>
        exception.Errors.Cast<SqlError>().Any(error => error.Number is 1205 or 2601 or 2627 or 3960);

    private async Task<Session> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await dbContext.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Session was not found.");

    private Task<RsvpResponse?> FindRsvpAsync(Guid sessionId, Guid playerProfileId, CancellationToken cancellationToken) =>
        dbContext.RsvpResponses.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.PlayerProfileId == playerProfileId, cancellationToken);

    private Task<WaitlistEntry?> FindActiveWaitlistEntryAsync(Guid sessionId, Guid playerProfileId, CancellationToken cancellationToken) =>
        dbContext.WaitlistEntries.SingleOrDefaultAsync(
            x => x.SessionId == sessionId
                && x.PlayerProfileId == playerProfileId
                && x.Status == WaitlistEntryStatus.Active,
            cancellationToken);

    private async Task<RsvpResponse> UpsertRsvpAsync(
        RsvpResponse? rsvp,
        Guid sessionId,
        Guid playerProfileId,
        RsvpStatus status,
        CancellationToken cancellationToken)
    {
        if (rsvp is null)
        {
            rsvp = new RsvpResponse
            {
                SessionId = sessionId,
                PlayerProfileId = playerProfileId
            };
            await dbContext.RsvpResponses.AddAsync(rsvp, cancellationToken);
        }

        rsvp.Status = status;
        rsvp.IsDeleted = false;
        return rsvp;
    }

    private async Task<WaitlistEntry> AddWaitlistEntryAsync(Guid sessionId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        var nextPosition = await dbContext.WaitlistEntries
            .Where(x => x.SessionId == sessionId)
            .MaxAsync(x => (int?)x.Position, cancellationToken) ?? 0;

        var waitlistEntry = new WaitlistEntry
        {
            SessionId = sessionId,
            PlayerProfileId = playerProfileId,
            Position = nextPosition + 1,
            Status = WaitlistEntryStatus.Active
        };
        await dbContext.WaitlistEntries.AddAsync(waitlistEntry, cancellationToken);
        return waitlistEntry;
    }

    private async Task<WaitlistEntry?> PromoteNextEligibleAsync(
        Guid sessionId,
        Func<Guid, CancellationToken, Task<bool>> isEligibleForPromotion,
        CancellationToken cancellationToken)
    {
        var entries = await dbContext.WaitlistEntries
            .Where(x => x.SessionId == sessionId && x.Status == WaitlistEntryStatus.Active)
            .OrderBy(x => x.Position)
            .ToArrayAsync(cancellationToken);

        foreach (var entry in entries)
        {
            if (!await isEligibleForPromotion(entry.PlayerProfileId, cancellationToken))
            {
                entry.Status = WaitlistEntryStatus.Expired;
                continue;
            }

            entry.Status = WaitlistEntryStatus.Promoted;
            await UpsertRsvpAsync(
                await FindRsvpAsync(sessionId, entry.PlayerProfileId, cancellationToken),
                sessionId,
                entry.PlayerProfileId,
                RsvpStatus.Going,
                cancellationToken);
            await AddPlayerWaitlistPromotedOutboxMessageAsync(entry, cancellationToken);
            return entry;
        }

        return null;
    }

    private async Task AddPlayerWaitlistPromotedOutboxMessageAsync(
        WaitlistEntry entry,
        CancellationToken cancellationToken)
    {
        var promotedAtUtc = clock.UtcNow;

        await dbContext.OutboxMessages.AddAsync(
            new OutboxMessage
            {
                MessageType = "PlayerWaitlistPromoted",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    entry.SessionId,
                    entry.PlayerProfileId,
                    WaitlistEntryId = entry.Id,
                    PromotedAtUtc = promotedAtUtc
                }),
                Status = OutboxMessageStatus.Pending,
                AvailableAtUtc = promotedAtUtc
            },
            cancellationToken);
    }

    private static void CancelWaitlist(WaitlistEntry? waitlistEntry)
    {
        if (waitlistEntry is null)
        {
            return;
        }

        waitlistEntry.Status = WaitlistEntryStatus.Canceled;
    }
}
