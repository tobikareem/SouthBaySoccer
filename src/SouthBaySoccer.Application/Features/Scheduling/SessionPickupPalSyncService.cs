using System.Text.Json;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Rsvps;
using SouthBaySoccer.Domain.Entities.Groups;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Scheduling;

/// <summary>Outbox message types written by the session game sync.</summary>
public static class SessionOutboxMessages
{
    /// <summary>
    /// An app-managed session must be mirrored onto Pickup Pal (game created, updated, or
    /// terminated). Payload: <c>{ SessionId, Action, ActingPlayerProfileId, RequestedAtUtc }</c>.
    /// The action in the payload is informational; the processor re-derives it from the session's
    /// current local state so the last local write always wins.
    /// </summary>
    public const string SessionPickupPalSyncRequested = "SessionPickupPalSyncRequested";
}

/// <summary>Safe error codes persisted on <c>Sessions.PickupPalSyncError</c> and in outbox reasons.</summary>
public static class SessionPickupPalSyncErrorCodes
{
    /// <summary>Pickup Pal no longer has the game.</summary>
    public const string GameNotFound = PickupPalSyncErrorCodes.GameNotFound;

    /// <summary>Pickup Pal rejected the request for a non-retryable reason (for example a 400 with a message).</summary>
    public const string Rejected = PickupPalSyncErrorCodes.Rejected;

    /// <summary>Pickup Pal could not be reached or refused the call (retryable).</summary>
    public const string Unavailable = PickupPalSyncErrorCodes.Unavailable;

    /// <summary>An unexpected error interrupted the push (retryable).</summary>
    public const string Unexpected = PickupPalSyncErrorCodes.Unexpected;

    /// <summary>The acting admin has no Pickup Pal user id, so Pickup Pal has no creator for the game (terminal).</summary>
    public const string MissingCreator = "MissingCreator";

    /// <summary>The session's group has no Pickup Pal group id (terminal).</summary>
    public const string MissingGroup = "MissingGroup";

    /// <summary>Pickup Pal answered success without a readable game id (terminal: a retry could duplicate the game).</summary>
    public const string InvalidResponse = "InvalidResponse";
}

/// <summary>What Pickup Pal should do for a session, derived from the session's current local state.</summary>
public enum SessionPickupPalSyncAction
{
    /// <summary>Nothing to mirror: draft, app-only, imported (Pickup Pal owns it), or no game to terminate.</summary>
    None,

    /// <summary>The session is published with a group but has no game yet: create one.</summary>
    EnsureCreated,

    /// <summary>The session is published and has an app-created game: push location, capacity, and start.</summary>
    EnsureUpdated,

    /// <summary>The session is canceled or deleted and has an app-created game: terminate it.</summary>
    EnsureTerminated,
}

/// <summary>
/// Mirrors app-managed sessions onto Pickup Pal as WhatsApp-group games. Runs strictly after the
/// local session write has committed and never fails the admin's request: every failure ends as a
/// persisted sync status on the session plus an outbox row that the timer-driven processor retries.
/// </summary>
public interface ISessionPickupPalSyncService
{
    /// <summary>
    /// Immediate path used by the session admin handlers: records a pending outbox row, pushes the
    /// session's current state, and settles the row. Returns the status to report; sessions with
    /// nothing to mirror return <see cref="PickupPalSyncStatus.NotApplicable"/> without touching
    /// the database.
    /// </summary>
    Task<PickupPalSyncStatus> SyncAfterLocalWriteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shared core used by the outbox processor: re-derives the action from the session's current
    /// local state, pushes it, and updates the session's sync columns. Tracked changes are
    /// <b>not saved</b> (except the created game id, which must be durable before the game is
    /// re-read); the caller commits them together with the outbox row.
    /// </summary>
    Task<PickupPalSyncOutcome> PushCurrentStateAsync(
        Guid sessionId,
        Guid? actingPlayerProfileId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISessionPickupPalSyncService"/>
public sealed class SessionPickupPalSyncService(
    ISessionRepository sessionRepository,
    IGroupChatRepository groupChatRepository,
    IVenueRepository venueRepository,
    IPlayerProfileRepository playerProfileRepository,
    IPickupPalGameRepository gameRepository,
    IPickupPalGamesClient gamesClient,
    IPickupPalGameImportService importService,
    IOutboxMessageRepository outboxRepository,
    IUnitOfWork unitOfWork,
    SessionPickupPalSyncGate gate,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<SessionPickupPalSyncService> logger) : ISessionPickupPalSyncService
{
    /// <summary>Delay before the processor retries a push that failed on the immediate path.</summary>
    private static readonly TimeSpan ImmediateRetryDelay = TimeSpan.FromMinutes(1);

    public async Task<PickupPalSyncStatus> SyncAfterLocalWriteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Serialize pushes per session on this instance: a create is not idempotent on Pickup
            // Pal's side, so an update racing a publish must observe the stored game id. The
            // session is read only once the lease is held so it reflects the earlier push.
            using var lease = await gate.AcquireAsync(sessionId, cancellationToken);
            var session = await sessionRepository.FindForPickupPalSyncAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return PickupPalSyncStatus.NotApplicable;
            }

            var action = ResolveAction(session);
            if (action == SessionPickupPalSyncAction.None)
            {
                return PickupPalSyncStatus.NotApplicable;
            }

            var actingPlayerProfileId = await ResolveActingProfileIdAsync(cancellationToken);
            var now = clock.UtcNow;

            // Write before call: the outbox row and the Pending status are committed before Pickup
            // Pal is contacted, so a crash or timeout mid-push leaves a retryable record.
            var pending = new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, null);
            var (message, lockedByProcessor) = await OpenOutboxMessageAsync(session.Id, action, actingPlayerProfileId, now, cancellationToken);
            ApplyToSession(session, pending, now);
            if (!await TrySaveAsync(cancellationToken))
            {
                // Another writer moved a row version: the processor settled this row a moment ago,
                // or a concurrent admin write. Re-read both rows and reopen the outbox row against
                // their current versions so this write always leaves a retry row behind; the
                // processor's next run pushes the current local state.
                session = await ReloadAsync(sessionId, cancellationToken);
                await OpenOutboxMessageAsync(session.Id, action, actingPlayerProfileId, now, cancellationToken);
                ApplyToSession(session, pending, now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return PickupPalSyncStatus.Pending;
            }

            if (lockedByProcessor)
            {
                // The processor is pushing this session right now. Unlike the roster sync, this
                // request does not push as well - two concurrent creates would leave two games on
                // Pickup Pal. The row was reopened above, so the processor's settle loses the
                // row-version race and the next run pushes the current local state.
                return PickupPalSyncStatus.Pending;
            }

            // Execute hands back the live session: a create that lost a row-version race re-reads
            // the session, and every later write must land on that instance.
            (var outcome, session) = await ExecuteAsync(session, action, actingPlayerProfileId, cancellationToken);
            var settledAtUtc = clock.UtcNow;
            ApplyToSession(session, outcome, settledAtUtc);
            SettleOutboxMessage(message, outcome, settledAtUtc);
            if (!await TrySaveAsync(cancellationToken))
            {
                session = await ReloadAsync(sessionId, cancellationToken);
                ApplyToSession(session, outcome, settledAtUtc);
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
            // The local session write is committed, but the sync bookkeeping could not be
            // persisted, so there may be no outbox row to retry from: report Failed, not Pending.
            logger.LogWarning(
                "Pickup Pal game sync could not persist its state after the local session write committed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
            unitOfWork.DiscardChanges();
            await TryRecordUnexpectedFailureAsync(sessionId, cancellationToken);
            return PickupPalSyncStatus.Failed;
        }
    }

    public async Task<PickupPalSyncOutcome> PushCurrentStateAsync(
        Guid sessionId,
        Guid? actingPlayerProfileId,
        CancellationToken cancellationToken = default)
    {
        // Lease first, read second: a processor claim overlapping an in-flight create on this
        // instance must see the stored game id, or it would re-derive EnsureCreated from a stale
        // row and POST a second game.
        using var lease = await gate.AcquireAsync(sessionId, cancellationToken);
        var session = await sessionRepository.FindForPickupPalSyncAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return new PickupPalSyncOutcome(PickupPalSyncStatus.NotApplicable, null);
        }

        var action = ResolveAction(session);
        if (action == SessionPickupPalSyncAction.None)
        {
            return new PickupPalSyncOutcome(PickupPalSyncStatus.NotApplicable, null);
        }

        (var outcome, session) = await ExecuteAsync(session, action, actingPlayerProfileId, cancellationToken);
        ApplyToSession(session, outcome, clock.UtcNow);
        return outcome;
    }

    /// <summary>
    /// Derives what Pickup Pal should do from the session as it is now. Imported games belong to
    /// Pickup Pal and are never created or terminated from here; drafts never push; only a
    /// published session with a group gets a game, and only a session that has one gets updates
    /// or a termination.
    /// </summary>
    public static SessionPickupPalSyncAction ResolveAction(Session session)
    {
        if (session.PickupPalOrigin == PickupPalOrigin.Imported)
        {
            return SessionPickupPalSyncAction.None;
        }

        var hasGame = !string.IsNullOrWhiteSpace(session.PickupPalGameId);
        if (session.IsDeleted || session.Status == SessionStatus.Canceled)
        {
            return hasGame ? SessionPickupPalSyncAction.EnsureTerminated : SessionPickupPalSyncAction.None;
        }

        if (session.Status != SessionStatus.Published)
        {
            return SessionPickupPalSyncAction.None;
        }

        if (hasGame)
        {
            return SessionPickupPalSyncAction.EnsureUpdated;
        }

        return session.GroupChatId is null
            ? SessionPickupPalSyncAction.None
            : SessionPickupPalSyncAction.EnsureCreated;
    }

    /// <summary>
    /// Runs the action and returns the outcome together with the live session instance: a create
    /// that lost a row-version race while storing the game id re-reads the session, and callers
    /// must apply their later writes to that instance, never the detached one.
    /// </summary>
    private async Task<(PickupPalSyncOutcome Outcome, Session Session)> ExecuteAsync(
        Session session,
        SessionPickupPalSyncAction action,
        Guid? actingPlayerProfileId,
        CancellationToken cancellationToken)
    {
        try
        {
            return action switch
            {
                SessionPickupPalSyncAction.EnsureCreated => await CreateAsync(session, actingPlayerProfileId, cancellationToken),
                SessionPickupPalSyncAction.EnsureUpdated => (await UpdateAsync(session, cancellationToken), session),
                SessionPickupPalSyncAction.EnsureTerminated => (await TerminateAsync(session, cancellationToken), session),
                _ => (new PickupPalSyncOutcome(PickupPalSyncStatus.NotApplicable, null), session),
            };
        }
        catch (ApplicationServiceUnavailableException)
        {
            return (new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, SessionPickupPalSyncErrorCodes.Unavailable), session);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Pickup Pal game push failed unexpectedly. Action: {Action}, ExceptionType: {ExceptionType}",
                action,
                exception.GetType().Name);
            return (new PickupPalSyncOutcome(PickupPalSyncStatus.Pending, SessionPickupPalSyncErrorCodes.Unexpected), session);
        }
    }

    private async Task<(PickupPalSyncOutcome Outcome, Session Session)> CreateAsync(
        Session session,
        Guid? actingPlayerProfileId,
        CancellationToken cancellationToken)
    {
        var group = await FindGroupAsync(session, cancellationToken);
        if (group is null || string.IsNullOrWhiteSpace(group.ExternalId))
        {
            return (Failed(SessionPickupPalSyncErrorCodes.MissingGroup), session);
        }

        var creatorId = await ResolveCreatorIdAsync(actingPlayerProfileId, cancellationToken);
        if (creatorId is null)
        {
            return (Failed(SessionPickupPalSyncErrorCodes.MissingCreator), session);
        }

        var request = new PickupPalGameCreateRequest(
            group.ExternalId,
            session.StartsAtUtc,
            TimeZoneIdOf(group),
            await BuildLocationAsync(session, cancellationToken),
            session.Capacity,
            creatorId);

        var created = await gamesClient.CreateGameAsync(request, cancellationToken);
        if (created.Result != PickupPalGameWriteResult.Applied || string.IsNullOrWhiteSpace(created.GameId))
        {
            var code = created.Result == PickupPalGameWriteResult.Applied
                ? SessionPickupPalSyncErrorCodes.InvalidResponse
                : ToErrorCode(created.Result);
            return (Failed(code), session);
        }

        // The game id must be durable before anything else happens: the refresh below re-reads
        // the game through the import path, which matches sessions by game id in the database,
        // and a retry after a crash must find the id rather than create a second game.
        session = await PersistCreatedGameAsync(session, created.GameId, cancellationToken);
        await TryRefreshGameAsync(created.GameId, cancellationToken);
        return (Synced(), session);
    }

    private async Task<PickupPalSyncOutcome> UpdateAsync(Session session, CancellationToken cancellationToken)
    {
        var gameId = session.PickupPalGameId!; // ResolveAction only yields EnsureUpdated when the id is set.
        var group = await FindGroupAsync(session, cancellationToken);
        var snapshots = await gameRepository.ListSnapshotsByGameIdsAsync([gameId], cancellationToken);
        var snapshot = snapshots.FirstOrDefault();

        // Date/time/timezone travel only when the start changed relative to what Pickup Pal last
        // reported (the snapshot); a missing snapshot sends them too, which is harmless.
        var startChanged = snapshot is null || snapshot.StartsAtUtc != session.StartsAtUtc;
        var request = new PickupPalGameUpdateRequest(
            await BuildLocationAsync(session, cancellationToken),
            session.Capacity,
            startChanged ? session.StartsAtUtc : null,
            TimeZoneIdOf(group));

        var result = await gamesClient.UpdateGameAsync(gameId, request, cancellationToken);
        if (result == PickupPalGameWriteResult.GameNotFound)
        {
            // Pickup Pal no longer has the game: drop the link (the origin stays CreatedByApp) so
            // the next admin write resolves to EnsureCreated and recreates it.
            session.PickupPalGameId = null;
            if (string.Equals(session.OccurrenceKey, PickupPalOccurrenceKey.Build(gameId), StringComparison.Ordinal))
            {
                session.OccurrenceKey = null;
            }

            return Failed(SessionPickupPalSyncErrorCodes.GameNotFound);
        }

        if (result != PickupPalGameWriteResult.Applied)
        {
            return Failed(ToErrorCode(result));
        }

        await TryRefreshGameAsync(gameId, cancellationToken);
        return Synced();
    }

    private async Task<PickupPalSyncOutcome> TerminateAsync(Session session, CancellationToken cancellationToken)
    {
        var result = await gamesClient.TerminateGameAsync(session.PickupPalGameId!, cancellationToken);
        return result is PickupPalGameWriteResult.Applied or PickupPalGameWriteResult.GameNotFound
            ? Synced()
            : Failed(ToErrorCode(result));
    }

    /// <summary>Stores the created game id and returns the session instance that now carries it.</summary>
    private async Task<Session> PersistCreatedGameAsync(Session session, string gameId, CancellationToken cancellationToken)
    {
        ApplyCreatedGame(session, gameId);
        if (await TrySaveAsync(cancellationToken))
        {
            return session;
        }

        // Another writer moved the row version (an admin edit landing mid-publish): the game now
        // exists on Pickup Pal, so the id is re-applied to the fresh row rather than lost, and
        // that fresh row is what every later write must target.
        var fresh = await ReloadAsync(session.Id, cancellationToken);
        ApplyCreatedGame(fresh, gameId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return fresh;
    }

    private void ApplyCreatedGame(Session session, string gameId)
    {
        session.PickupPalGameId = gameId;
        session.PickupPalOrigin = PickupPalOrigin.CreatedByApp;
        // The game id is the link. The occurrence key mirrors it only for a session that has
        // none, so a recurrence occurrence keeps the key CreateSessionOccurrenceCommandHandler
        // dedupes on.
        session.OccurrenceKey ??= PickupPalOccurrenceKey.Build(gameId);
        sessionRepository.Update(session);
    }

    private async Task TryRefreshGameAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            var game = await gamesClient.GetGameAsync(gameId, cancellationToken);
            if (game is not null)
            {
                await importService.UpsertAsync([game], cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Best effort: the push succeeded, and the next active-games import owns the snapshot
            // from here, so a refresh failure must never turn a created game into a retry loop.
            logger.LogWarning(
                "Pickup Pal game refresh after a successful push failed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
        }
    }

    private Task<GroupChat?> FindGroupAsync(Session session, CancellationToken cancellationToken) =>
        session.GroupChatId is { } groupChatId
            ? groupChatRepository.GetByIdAsync(groupChatId, cancellationToken)
            : Task.FromResult<GroupChat?>(null);

    private async Task<string?> ResolveCreatorIdAsync(Guid? actingPlayerProfileId, CancellationToken cancellationToken)
    {
        if (actingPlayerProfileId is not { } profileId)
        {
            return null;
        }

        var profile = await playerProfileRepository.FindProfileAsync(profileId, cancellationToken);
        return string.IsNullOrWhiteSpace(profile?.PickupPalUserId) ? null : profile.PickupPalUserId;
    }

    private async Task<Guid?> ResolveActingProfileIdAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } identityUserId)
        {
            return null;
        }

        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken);
        return profile?.Id;
    }

    /// <summary>Venue name plus ", address" when the venue has one; the title if the venue row is gone.</summary>
    private async Task<string> BuildLocationAsync(Session session, CancellationToken cancellationToken)
    {
        var venue = await venueRepository.GetByIdAsync(session.VenueId, cancellationToken);
        if (venue is null)
        {
            return session.Title;
        }

        return string.IsNullOrWhiteSpace(venue.Address)
            ? venue.Name
            : $"{venue.Name}, {venue.Address.Trim()}";
    }

    private static string TimeZoneIdOf(GroupChat? group) =>
        string.IsNullOrWhiteSpace(group?.Timezone) ? SessionAdminTimeZone.DefaultTimeZoneId : group.Timezone.Trim();

    private static string ToErrorCode(PickupPalGameWriteResult result) => result switch
    {
        PickupPalGameWriteResult.GameNotFound => SessionPickupPalSyncErrorCodes.GameNotFound,
        PickupPalGameWriteResult.InvalidResponse => SessionPickupPalSyncErrorCodes.InvalidResponse,
        _ => SessionPickupPalSyncErrorCodes.Rejected,
    };

    private static PickupPalSyncOutcome Synced() => new(PickupPalSyncStatus.Synced, null);

    private static PickupPalSyncOutcome Failed(string code) => new(PickupPalSyncStatus.Failed, code);

    private async Task<Session> ReloadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await sessionRepository.FindForPickupPalSyncAsync(sessionId, cancellationToken)
        ?? throw new ApplicationNotFoundException("Session was not found.");

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

    private async Task TryRecordUnexpectedFailureAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionRepository.FindForPickupPalSyncAsync(sessionId, cancellationToken);
            if (session is null)
            {
                return;
            }

            ApplyToSession(session, Failed(SessionPickupPalSyncErrorCodes.Unexpected), clock.UtcNow);
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

    /// <summary>
    /// Creates or reopens the row for (session, action). A row the processor currently holds is
    /// reopened too - the caller then leaves the push to the processor - so that the processor's
    /// settle fails as a row-version conflict and the row is claimed again on the next run.
    /// </summary>
    private async Task<(OutboxMessage Message, bool LockedByProcessor)> OpenOutboxMessageAsync(
        Guid sessionId,
        SessionPickupPalSyncAction action,
        Guid? actingPlayerProfileId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // One row per (session, action): a repeated request reuses the row instead of tripping
        // the unique index, and the table stays bounded to three rows per session.
        var idempotencyKey = BuildIdempotencyKey(sessionId, action);
        var payloadJson = JsonSerializer.Serialize(new
        {
            SessionId = sessionId,
            Action = action.ToString(),
            ActingPlayerProfileId = actingPlayerProfileId,
            RequestedAtUtc = nowUtc,
        });

        var message = await outboxRepository.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (message is null)
        {
            message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = SessionOutboxMessages.SessionPickupPalSyncRequested,
                PayloadJson = payloadJson,
                Status = OutboxMessageStatus.Pending,
                AvailableAtUtc = nowUtc,
                IdempotencyKey = idempotencyKey,
            };
            await outboxRepository.AddAsync(message, cancellationToken);
            return (message, false);
        }

        var lockedByProcessor = message.Status == OutboxMessageStatus.Processing && message.LockedUntilUtc > nowUtc;

        // A new request reopens the row (including a dead-lettered one): the attempt budget starts
        // over because the admin just asked again.
        message.PayloadJson = payloadJson;
        message.Status = OutboxMessageStatus.Pending;
        message.AvailableAtUtc = nowUtc;
        message.AttemptCount = 0;
        message.LockToken = null;
        message.LockedUntilUtc = null;
        message.ProcessedAtUtc = null;
        message.DeadLetterReason = null;
        outboxRepository.Update(message);
        return (message, lockedByProcessor);
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

    private void ApplyToSession(Session session, PickupPalSyncOutcome outcome, DateTime nowUtc)
    {
        session.PickupPalSyncStatus = outcome.Status;
        if (outcome.Status == PickupPalSyncStatus.Synced)
        {
            session.PickupPalSyncedAtUtc = nowUtc;
            session.PickupPalSyncError = null;
        }
        else
        {
            session.PickupPalSyncError = outcome.ErrorCode;
        }

        sessionRepository.Update(session);
    }

    internal static string BuildIdempotencyKey(Guid sessionId, SessionPickupPalSyncAction action) =>
        $"{SessionOutboxMessages.SessionPickupPalSyncRequested}:{sessionId:D}:{action}";
}

/// <summary>
/// In-process, per-session async lock for game pushes. Registered as a singleton; the scope is
/// one Function instance (see the story design's Limits for the cross-instance case).
/// </summary>
public sealed class SessionPickupPalSyncGate : KeyedAsyncGate<Guid>;
