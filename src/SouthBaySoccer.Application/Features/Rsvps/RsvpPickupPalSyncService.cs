using System.Text.Json;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Rsvps;

/// <summary>Outbox message types written by the RSVP roster sync.</summary>
public static class RsvpOutboxMessages
{
    /// <summary>
    /// A player's RSVP on a Pickup Pal-imported session must be mirrored onto the Pickup Pal roster.
    /// Payload: <c>{ SessionId, PlayerProfileId, PickupPalGameId, DesiredState, RequestedAtUtc }</c>.
    /// The desired state in the payload is informational; the processor re-derives it from the
    /// player's current local RSVP so the last local write always wins.
    /// </summary>
    public const string RsvpPickupPalSyncRequested = "RsvpPickupPalSyncRequested";
}

/// <summary>Safe error codes persisted on <c>RsvpResponses.PickupPalSyncError</c> and in outbox reasons.</summary>
public static class PickupPalSyncErrorCodes
{
    /// <summary>Pickup Pal refused the add because the game is full.</summary>
    public const string GameFull = "GameFull";

    /// <summary>Pickup Pal no longer has the game.</summary>
    public const string GameNotFound = "GameNotFound";

    /// <summary>Pickup Pal rejected the request for another non-retryable reason.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Pickup Pal could not be reached or refused the call (retryable).</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>An unexpected error interrupted the push (retryable).</summary>
    public const string Unexpected = "Unexpected";
}

/// <summary>What the Pickup Pal roster should look like for the player, derived from local state.</summary>
public enum PickupPalRosterDesiredState
{
    /// <summary>The player is Going or waitlisted locally: they belong on the Pickup Pal roster.</summary>
    Add,

    /// <summary>The player is Maybe, NotGoing, or has no RSVP: they must not be on the Pickup Pal roster.</summary>
    Remove,
}

/// <summary>Outcome of one push. <see cref="PickupPalSyncStatus.Pending"/> means a retry is warranted.</summary>
public sealed record PickupPalSyncOutcome(PickupPalSyncStatus Status, string? ErrorCode)
{
    /// <summary>The push did not reach a terminal state and should be retried from the outbox.</summary>
    public bool IsRetryable => Status == PickupPalSyncStatus.Pending;
}

/// <summary>
/// Mirrors local RSVP state onto the Pickup Pal roster of imported games. Runs strictly after the
/// local RSVP transaction has committed and never fails the RSVP: every failure ends as a persisted
/// sync status plus an outbox row that the timer-driven processor retries.
/// </summary>
public interface IRsvpPickupPalSyncService
{
    /// <summary>
    /// Immediate path used by the RSVP handlers: records a pending outbox row, pushes the player's
    /// current local state, refreshes the game snapshot, and settles the row. Returns the status to
    /// report to the caller; app-only sessions and profiles without a Pickup Pal id return
    /// <see cref="PickupPalSyncStatus.NotApplicable"/> without touching the database.
    /// </summary>
    Task<PickupPalSyncStatus> SyncAfterLocalWriteAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shared core used by the outbox processor: re-derives the desired state from the current
    /// local RSVP, pushes it, refreshes the game, and updates the RSVP row's sync columns. Tracked
    /// changes are <b>not saved</b>; the caller commits them together with the outbox row.
    /// </summary>
    Task<PickupPalSyncOutcome> PushCurrentStateAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRsvpPickupPalSyncService"/>
public sealed class RsvpPickupPalSyncService(
    ISessionRepository sessionRepository,
    IPlayerProfileRepository playerProfileRepository,
    IRsvpRepository rsvpRepository,
    IPickupPalGamesClient gamesClient,
    IPickupPalGameImportService importService,
    IOutboxMessageRepository outboxRepository,
    IUnitOfWork unitOfWork,
    RsvpPickupPalSyncGate gate,
    IClock clock,
    ILogger<RsvpPickupPalSyncService> logger) : IRsvpPickupPalSyncService
{
    /// <summary>Delay before the processor retries a push that failed on the immediate path.</summary>
    private static readonly TimeSpan ImmediateRetryDelay = TimeSpan.FromMinutes(1);

    public async Task<PickupPalSyncStatus> SyncAfterLocalWriteAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var target = await ResolveTargetAsync(sessionId, playerProfileId, cancellationToken);
            if (target is null)
            {
                return PickupPalSyncStatus.NotApplicable;
            }

            // Serialize pushes per player and session on this instance so two rapid taps cannot
            // overtake each other; each push then sends the local state as it is right now.
            using var lease = await gate.AcquireAsync(sessionId, playerProfileId, cancellationToken);

            var desired = await ReadDesiredStateAsync(sessionId, playerProfileId, cancellationToken);
            var now = clock.UtcNow;

            // Write before call (same rule as account deletion): the outbox row and the Pending
            // status are committed before Pickup Pal is contacted, so a crash or timeout mid-push
            // leaves a retryable record rather than a silent gap.
            var pending = new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, null);
            var (message, owned) = await OpenOutboxMessageAsync(sessionId, playerProfileId, target, desired, now, cancellationToken);
            var rsvp = await rsvpRepository.FindRsvpForPickupPalSyncAsync(sessionId, playerProfileId, cancellationToken);
            ApplyToRsvp(rsvp, pending, now);
            if (!await TrySaveAsync(cancellationToken))
            {
                // The outbox row's version changed under us (the processor claimed it, or another
                // instance created it first): it belongs to that writer now. Re-track only the RSVP
                // row and keep going; the push below still sends the current local state.
                owned = false;
                rsvp = await rsvpRepository.FindRsvpForPickupPalSyncAsync(sessionId, playerProfileId, cancellationToken);
                ApplyToRsvp(rsvp, pending, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var outcome = await PushAsync(target, desired, cancellationToken);
            var settledAtUtc = clock.UtcNow;
            ApplyToRsvp(rsvp, outcome, settledAtUtc);
            if (owned)
            {
                SettleOutboxMessage(message, outcome, settledAtUtc);
            }

            if (!await TrySaveAsync(cancellationToken))
            {
                rsvp = await rsvpRepository.FindRsvpForPickupPalSyncAsync(sessionId, playerProfileId, cancellationToken);
                ApplyToRsvp(rsvp, outcome, settledAtUtc);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return outcome.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The local RSVP is committed, but the sync bookkeeping itself could not be persisted,
            // so there may be no outbox row to retry from: report Failed, not Pending, and record
            // it on the RSVP row if that much still works. Half-tracked state (a partly applied game
            // refresh, a stale outbox row) is dropped first so nothing of it leaks into a later save.
            logger.LogWarning(
                "Pickup Pal roster sync could not persist its state after the local RSVP committed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
            unitOfWork.DiscardChanges();
            await TryRecordUnexpectedFailureAsync(sessionId, playerProfileId, cancellationToken);
            return PickupPalSyncStatus.Failed;
        }
    }

    /// <summary>
    /// Saves, treating a concurrency or uniqueness conflict as "another writer owns the row": the
    /// tracked changes are discarded and the caller re-tracks what is still its own to write.
    /// </summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ApplicationConflictException)
        {
            unitOfWork.DiscardChanges();
            return false;
        }
    }

    private async Task TryRecordUnexpectedFailureAsync(Guid sessionId, Guid playerProfileId, CancellationToken cancellationToken)
    {
        try
        {
            var rsvp = await rsvpRepository.FindRsvpForPickupPalSyncAsync(sessionId, playerProfileId, cancellationToken);
            ApplyToRsvp(rsvp, new PickupPalSyncOutcome(PickupPalSyncStatus.Failed, PickupPalSyncErrorCodes.Unexpected), clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Best effort only; the caller already reports Failed.
            unitOfWork.DiscardChanges();
        }
    }

    public async Task<PickupPalSyncOutcome> PushCurrentStateAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(sessionId, playerProfileId, cancellationToken);
        if (target is null)
        {
            return new PickupPalSyncOutcome(PickupPalSyncStatus.NotApplicable, null);
        }

        using var lease = await gate.AcquireAsync(sessionId, playerProfileId, cancellationToken);

        var desired = await ReadDesiredStateAsync(sessionId, playerProfileId, cancellationToken);
        var rsvp = await rsvpRepository.FindRsvpForPickupPalSyncAsync(sessionId, playerProfileId, cancellationToken);
        var outcome = await PushAsync(target, desired, cancellationToken);
        ApplyToRsvp(rsvp, outcome, clock.UtcNow);
        return outcome;
    }

    /// <summary>
    /// A session is in scope only when its occurrence key marks it as imported from Pickup Pal, and
    /// a player can only be pushed with the Pickup Pal user id their profile carries.
    /// </summary>
    private async Task<SyncTarget?> ResolveTargetAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        // The stored game id is the link (app-created sessions keep their own occurrence key);
        // the occurrence-key convention remains as the fallback for rows imported before the
        // column existed.
        var gameId = session.PickupPalGameId;
        if (string.IsNullOrWhiteSpace(gameId) && !PickupPalOccurrenceKey.TryGetGameId(session.OccurrenceKey, out gameId))
        {
            return null;
        }

        var profile = await playerProfileRepository.FindProfileAsync(playerProfileId, cancellationToken);
        if (profile?.PickupPalUserId is not { Length: > 0 } pickupPalUserId)
        {
            return null;
        }

        return new SyncTarget(gameId, pickupPalUserId, profile.DisplayName);
    }

    private async Task<PickupPalRosterDesiredState> ReadDesiredStateAsync(
        Guid sessionId,
        Guid playerProfileId,
        CancellationToken cancellationToken)
    {
        var current = await rsvpRepository.GetMyRsvpAsync(sessionId, playerProfileId, cancellationToken);
        return current?.State is RsvpMutationState.Going or RsvpMutationState.Waitlisted
            ? PickupPalRosterDesiredState.Add
            : PickupPalRosterDesiredState.Remove;
    }

    private async Task<PickupPalSyncOutcome> PushAsync(
        SyncTarget target,
        PickupPalRosterDesiredState desired,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = desired == PickupPalRosterDesiredState.Add
                ? await gamesClient.AddPlayerAsync(target.GameId, target.PickupPalUserId, target.DisplayName, cancellationToken)
                : await gamesClient.RemovePlayerAsync(target.GameId, target.PickupPalUserId, cancellationToken);

            switch (result)
            {
                case PickupPalRosterPushResult.Applied:
                case PickupPalRosterPushResult.AlreadyApplied:
                    // Pickup Pal decided where the player landed (going list or its waitlist); the
                    // refresh is what makes that decision visible locally, so a refresh failure is
                    // retryable even though the push itself succeeded (re-pushing is idempotent).
                    await RefreshGameAsync(target.GameId, cancellationToken);
                    return new PickupPalSyncOutcome(PickupPalSyncStatus.Synced, null);
                case PickupPalRosterPushResult.GameFull:
                    await TryRefreshGameAsync(target.GameId, cancellationToken);
                    return new PickupPalSyncOutcome(PickupPalSyncStatus.Failed, PickupPalSyncErrorCodes.GameFull);
                case PickupPalRosterPushResult.GameNotFound:
                    return new PickupPalSyncOutcome(PickupPalSyncStatus.Failed, PickupPalSyncErrorCodes.GameNotFound);
                default:
                    return new PickupPalSyncOutcome(PickupPalSyncStatus.Failed, PickupPalSyncErrorCodes.Rejected);
            }
        }
        catch (ApplicationServiceUnavailableException)
        {
            return new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, PickupPalSyncErrorCodes.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Pickup Pal roster push failed unexpectedly. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
            return new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, PickupPalSyncErrorCodes.Unexpected);
        }
    }

    private async Task RefreshGameAsync(string gameId, CancellationToken cancellationToken)
    {
        var game = await gamesClient.GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            // The game disappeared between the push and the read; the next active-games import
            // owns the session from here.
            return;
        }

        await importService.UpsertAsync([game], cancellationToken);
    }

    private async Task TryRefreshGameAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshGameAsync(gameId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Best effort: the terminal outcome stands whether or not the snapshot refreshed, so a
            // refresh failure must never turn GameFull/GameNotFound into a retry loop.
            logger.LogWarning(
                "Pickup Pal game refresh after a terminal roster outcome failed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
        }
    }

    private async Task<(OutboxMessage Message, bool Owned)> OpenOutboxMessageAsync(
        Guid sessionId,
        Guid playerProfileId,
        SyncTarget target,
        PickupPalRosterDesiredState desired,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // One row per (session, player, desired state): the key bounds the table to two rows per
        // RSVP and lets a repeated request reuse the row instead of tripping the unique index.
        var idempotencyKey = BuildIdempotencyKey(sessionId, playerProfileId, desired);
        var payloadJson = JsonSerializer.Serialize(new
        {
            SessionId = sessionId,
            PlayerProfileId = playerProfileId,
            PickupPalGameId = target.GameId,
            DesiredState = desired.ToString(),
            RequestedAtUtc = nowUtc,
        });

        var message = await outboxRepository.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (message is null)
        {
            message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = RsvpOutboxMessages.RsvpPickupPalSyncRequested,
                PayloadJson = payloadJson,
                Status = OutboxMessageStatus.Pending,
                AvailableAtUtc = nowUtc,
                IdempotencyKey = idempotencyKey,
            };
            await outboxRepository.AddAsync(message, cancellationToken);
            return (message, true);
        }

        if (message.Status == OutboxMessageStatus.Processing && message.LockedUntilUtc > nowUtc)
        {
            // The processor holds this row right now and will push the current local state itself;
            // this request still pushes immediately but leaves the row to its owner.
            return (message, false);
        }

        // A new request reopens the row (including a dead-lettered one): the attempt budget starts
        // over because the player just asked again.
        message.PayloadJson = payloadJson;
        message.Status = OutboxMessageStatus.Pending;
        message.AvailableAtUtc = nowUtc;
        message.AttemptCount = 0;
        message.LockToken = null;
        message.LockedUntilUtc = null;
        message.ProcessedAtUtc = null;
        message.DeadLetterReason = null;
        outboxRepository.Update(message);
        return (message, true);
    }

    private static void SettleOutboxMessage(OutboxMessage message, PickupPalSyncOutcome outcome, DateTime nowUtc)
    {
        if (outcome.IsRetryable)
        {
            message.Status = OutboxMessageStatus.RetryScheduled;
            message.AvailableAtUtc = nowUtc.Add(ImmediateRetryDelay);
            message.AttemptCount += 1;
        }
        else
        {
            message.Status = OutboxMessageStatus.Processed;
            message.ProcessedAtUtc = nowUtc;
        }

        message.LockToken = null;
        message.LockedUntilUtc = null;
    }

    private void ApplyToRsvp(RsvpResponse? rsvp, PickupPalSyncOutcome outcome, DateTime nowUtc)
    {
        if (rsvp is null)
        {
            // Waitlisted with no RSVP row (never had one): the outcome is reported to the caller and
            // carried by the outbox row only.
            return;
        }

        rsvp.PickupPalSyncStatus = outcome.Status;
        if (outcome.Status == PickupPalSyncStatus.Synced)
        {
            rsvp.PickupPalSyncedAtUtc = nowUtc;
            rsvp.PickupPalSyncError = null;
        }
        else
        {
            rsvp.PickupPalSyncError = outcome.ErrorCode;
        }

        rsvpRepository.UpdateRsvp(rsvp);
    }

    internal static string BuildIdempotencyKey(Guid sessionId, Guid playerProfileId, PickupPalRosterDesiredState desired) =>
        $"{RsvpOutboxMessages.RsvpPickupPalSyncRequested}:{sessionId:D}:{playerProfileId:D}:{desired}";

    private sealed record SyncTarget(string GameId, string PickupPalUserId, string DisplayName);
}

/// <summary>
/// In-process, per-(session, player) async lock for roster pushes. Registered as a singleton; the
/// scope is one Function instance (see the story design's Limits for the cross-instance case).
/// </summary>
public sealed class RsvpPickupPalSyncGate : KeyedAsyncGate<(Guid SessionId, Guid PlayerProfileId)>
{
    /// <summary>Waits for exclusive access to the key; dispose the lease to release it.</summary>
    public Task<IDisposable> AcquireAsync(Guid sessionId, Guid playerProfileId, CancellationToken cancellationToken = default) =>
        AcquireAsync((sessionId, playerProfileId), cancellationToken);
}
