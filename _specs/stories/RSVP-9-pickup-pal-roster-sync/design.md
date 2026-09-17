# RSVP-9 - Pickup Pal roster sync - Design

Realizes [`requirements.md`](requirements.md). RSVP transaction mechanics are in
[`../../design.md`](../../design.md) and `.ai/memory/m7-rsvp-waitlist.md`; the import is described
in `.ai/memory/pickuppal-games-import.md`. Distilled rules for agents:
`.ai/memory/m14-pickuppal-roster-sync.md`.

## Principles

1. **Local first, never rolled back.** The existing serializable RSVP transaction
   (`IRsvpRepository.SubmitRsvpAsync` / `CancelAndPromoteAsync` / `AddWithAdminOverrideAsync`)
   commits exactly as today. Everything below runs *after* that commit and can only change the
   sync columns on `RsvpResponses`, the outbox, and the imported snapshot.
2. **Pickup Pal is the source of truth for imported games.** After a successful push we re-read the
   single game and upsert it through the same code the import uses, so Pickup Pal's own
   waitlist/capacity decision lands in `PickupPalGameSnapshot` / `PickupPalGameParticipant` and the
   combined attendance projection.
3. **Desired state is derived, never carried.** Both the immediate push and the outbox retry
   compute "add" or "remove" from the player's *current* local state at the moment the push starts.
   That is what makes retries and out-of-order rows last-write-wins.
4. **Scope by occurrence key.** Only `Session.OccurrenceKey` starting with `pickuppal:` is synced
   (`PickupPalOccurrenceKey.TryGetGameId`). Only profiles with `PickupPalUserId` can be pushed.

## Components

| Layer | Type | Responsibility |
|---|---|---|
| Domain | `PickupPalSyncStatus` enum; `RsvpResponse.PickupPalSyncStatus/PickupPalSyncedAtUtc/PickupPalSyncError`; `RsvpMutationResult.PickupPalSyncStatus` | Persisted sync state (`NotApplicable`, `Synced`, `Pending`, `Failed`). Error is a safe code (`GameFull`, `GameNotFound`, `Rejected`, `Unavailable`, `Unexpected`), never a payload. |
| Domain | `IRsvpRepository.FindRsvpForPickupPalSyncAsync` + `UpdateRsvp` | Reads the player's latest RSVP row for a session **including soft-deleted rows** (cancel soft-deletes the row, and the sync outcome still belongs on it). |
| Domain | `IOutboxMessageRepository.ClaimDueAsync` | Atomic per-row claim (`Status` in Pending/RetryScheduled and due, or Processing with an expired lock) using `LockToken` / `LockedUntilUtc`; safe under concurrent instances. |
| Application | `IPickupPalGamesClient.GetGameAsync/AddPlayerAsync/RemovePlayerAsync`, `PickupPalRosterPushResult` | Port extension. Retryable failures throw `ApplicationServiceUnavailableException`; final outcomes are values. |
| Application | `PickupPalGameImportService` (extracted from `ImportPickupPalGamesCommandHandler`) | The per-game upsert (season lookup, venue, session, snapshot, participants, profile resolution). The import handler now only fetches the active feed and calls it; the sync calls it with one game. |
| Application | `IRsvpPickupPalSyncService` / `RsvpPickupPalSyncService` | `SyncAfterLocalWriteAsync` (immediate path) and `PushCurrentStateAsync` (shared core used by the outbox handler). |
| Application | `RsvpPickupPalSyncGate` (singleton) | In-process per-(session, player) async lock so two rapid taps on one instance push in order. |
| Application | `IOutboxMessageHandler`, `RsvpPickupPalSyncOutboxHandler`, `PickupPalUserDeletionOutboxHandler` | Executes one claimed row; returns Completed / Retry(code) / Fail(code). |
| Infrastructure | `PickupPalGamesClient` (extended), `RsvpRepository`, `OutboxMessageRepository` | HTTP + EF implementations. The games client keeps the URI-logging ban (no `ILogger`, no handlers) and sends `X-Api-Key` when `PickupPal:ApiKey` is set. |
| Infrastructure | Migration `AddRsvpPickupPalSync` | Adds the three columns to `RsvpResponses` (default `NotApplicable`). Controlled deploy only. |
| Functions | `OutboxFunctions.ProcessOutbox` (TimerTrigger, every 5 minutes), `OutboxProcessor`, `OutboxOptions` | Claims due rows for the two message types, runs the handler, applies backoff / dead-letter, saves per row. `Outbox:Enabled` (default true) short-circuits the run. |
| Contracts | `RsvpResponseDto.PickupPalSync` (string, default `"NotApplicable"`) | Additive; the MAUI client keeps compiling unchanged. |

## Flow - immediate path (SubmitRsvp / CancelRsvp / AdminOverrideRsvp)

```text
handler
  -> existing serializable RSVP transaction commits (unchanged)
  -> IRsvpPickupPalSyncService.SyncAfterLocalWriteAsync(sessionId, playerProfileId)
       session.OccurrenceKey not "pickuppal:*"      -> NotApplicable (no writes)
       profile.PickupPalUserId is null              -> NotApplicable (no writes)
       acquire RsvpPickupPalSyncGate(session, player)
       read current local state (GetMyRsvpAsync)    -> Going/Waitlisted => Add, else => Remove
       write-before-call:
         outbox row RsvpPickupPalSyncRequested (idempotency key
           "RsvpPickupPalSyncRequested:{sessionId}:{playerProfileId}:{Add|Remove}",
           reopened to Pending when it already exists and is not locked by the processor)
         RsvpResponse.PickupPalSyncStatus = Pending
         SaveChanges
       PushCurrentStateAsync (below)
       outbox row -> Processed (final outcome) or RetryScheduled (+1 min)
       SaveChanges
       a concurrency conflict on either save (OutboxMessage and RsvpResponse both carry a SQL
         RowVersion) means the processor or another instance owns the row: tracked state is
         discarded (IUnitOfWork.DiscardChanges), only the RSVP row is re-tracked and saved, and the
         push still sends the current local state
       any other exception is logged by type only, tracked state is discarded, the RSVP row is
         marked Failed(Unexpected) best effort, and Failed is reported; the RSVP never fails
  -> RsvpResultModel.PickupPalSync = outcome
CancelRsvp additionally syncs the player promoted from the local waitlist (Add), best effort.
```

## Flow - shared core (`PushCurrentStateAsync`)

```text
desired = Add    -> AddPlayerAsync(gameId, pickupPalUserId, profile.DisplayName)
desired = Remove -> RemovePlayerAsync(gameId, pickupPalUserId)
Applied / AlreadyApplied -> GetGameAsync(gameId); if found, PickupPalGameImportService.UpsertAsync([game])
                            -> Synced (SyncedAtUtc = now, error = null)
GameFull                 -> Failed(GameFull); refresh the game anyway so the snapshot shows Pickup Pal's fullness
GameNotFound             -> Failed(GameNotFound); no refresh (the next active-games import owns the session state)
Rejected (other 4xx)     -> Failed(Rejected)
ApplicationServiceUnavailableException (401/403/5xx/timeout/network) -> Pending(Unavailable)  [retryable]
any other exception      -> Pending(Unexpected)  [retryable]
```

Only the retryable outcomes leave the outbox row open. `Failed` is terminal until the player
changes their RSVP again, which writes a fresh request.

## Pickup Pal error handling (`PickupPalGamesClient`)

Both error body shapes are parsed (`{ "error": "text" }` and `{ "error": { "message": "text" } }`).
Matching is case-insensitive substring on the resolved message:

| Call | Response | Mapping |
|---|---|---|
| add | 2xx | `Applied` |
| add | 4xx, message contains `already` | `AlreadyApplied` |
| add | 4xx, message contains `full` | `GameFull` |
| add | 4xx, message contains `player`/`user`/`participant` + `not found` (Pickup Pal does not know the user id) | `Rejected` |
| add | 404 (any other message), or 4xx message containing `game` + `not found` | `GameNotFound` |
| add | other 4xx | `Rejected` |
| remove | 2xx | `Applied` |
| remove | 4xx whose message contains `not in`, `not a participant`, `not on`, or `player`/`user`/`participant` + `not found` (checked first: "Player not found in game" mentions both) | `AlreadyApplied` (the player is not on the roster, which is the desired state) |
| remove | 4xx message containing `game` + `not found` | `GameNotFound` |
| remove | 404 with any other message | `AlreadyApplied` |
| remove | other 4xx | `Rejected` |
| get | 404 | `null` |
| any | 401, 403, 5xx, timeout, network failure, unparsable success body | `ApplicationServiceUnavailableException` (retryable) |

These strings are assumptions until Pickup Pal documents the Games error catalogue; they are
listed in `.ai/memory/m14-pickuppal-roster-sync.md` so they can be corrected in one place.

## Outbox processor

- `[TimerTrigger("0 */5 * * * *")]`; the cadence is a compile-time constant because a missing
  `%setting%` breaks host indexing. `Outbox:Enabled` (default `true`), `Outbox:BatchSize` (50),
  `Outbox:LockDuration` (5 min), `Outbox:MaxAttempts` (6) are configuration.
- Claim: `ClaimDueAsync` (types from the static `OutboxMessageTypes.Handled` list) selects
  candidates (due Pending/RetryScheduled, or Processing whose lock expired) ordered by
  `AvailableAtUtc, CreatedAt`, then issues one conditional `UPDATE` per row that sets
  `Status = Processing`, `LockToken`, `LockedUntilUtc`. A row another instance claimed first affects
  zero rows. Claimed rows are then loaded by `LockToken == token && Status == Processing` alone, so
  an ambiguous UPDATE that the retry strategy re-ran cannot orphan a row this run actually owns.
- Execute: the handler for `MessageType` runs in its own scope. `RsvpPickupPalSyncRequested`
  re-derives the desired state from the current local RSVP and calls `PushCurrentStateAsync`;
  `PickupPalUserDeletionRequested` calls `IPickupPalOnboardingClient.DeleteUserAsync` (404 = already
  gone). A handler that throws is settled in a fresh scope so none of its half-tracked state is
  committed with the row.
- Writer races: `OutboxMessages` carries a SQL `RowVersion`. If the immediate RSVP path reopened the
  row after the claim, the processor's settle fails as a concurrency conflict and is skipped (the
  other writer owns the row); the mirror case on the immediate path is described above.
- Outcome: Completed -> `Processed`, `ProcessedAtUtc`, lock cleared. Retry -> `AttemptCount++`,
  `RetryScheduled` with `AvailableAtUtc = now + backoff(AttemptCount)` where backoff is
  1, 5, 15, 60, 60, ... minutes; when `AttemptCount >= MaxAttempts` the row becomes `DeadLettered`
  with `DeadLetterReason = "MaxAttemptsExceeded:{code}"`. Fail -> `DeadLettered` immediately with
  the code. Unknown message types are dead-lettered with `UnknownMessageType`. Each row is saved
  on its own so one failure cannot lose the others' progress.
- Idempotency: every handler is safe to re-run (add/remove are idempotent on Pickup Pal's side by
  the mapping above; delete treats 404 as success).

## Persistence

`RsvpResponses` gains `PickupPalSyncStatus nvarchar(32) not null default 'NotApplicable'`,
`PickupPalSyncedAtUtc datetime2 null`, `PickupPalSyncError nvarchar(64) null`; `OutboxMessages`
gains `RowVersion rowversion` (writer-race detection). One migration, `AddRsvpPickupPalSync`;
applied by the controlled release step only.

## Logging

The `HttpClientFactory` default `LoggingHttpMessageHandler` logs outbound request URIs at
Information, which for the Pickup Pal clients would include phone digits, tokens, emails, and user
or game ids. `AddInfrastructure` therefore calls `RemoveAllLoggers()` on every Pickup Pal
`AddHttpClient` chain, attaches no other handlers, and `host.json` sets `System.Net.Http.HttpClient`
to `Warning`; an Infrastructure test walks each built pipeline and asserts only the lifetime
tracker and the primary handler remain.

## Limits (documented, accepted)

- The per-player gate is per Function instance. Two opposite taps that land on two instances can
  push out of order; because each push sends the *current* local state, the later of the two local
  writes is what the later push sends, but if the pushes themselves reorder on the wire Pickup Pal
  can end up one step behind. The next RSVP change or outbox retry for that player repairs it. A
  cross-instance fix would need a Pickup Pal-side version or a distributed lock; deferred.
- A player who is waitlisted locally with no `RsvpResponse` row (never had one) has no row to carry
  the sync columns; the outcome is returned in the response and recorded in the outbox only.
- `OutboxMessages` rows are never purged (same gap as the other operational tables).
- A `DeadLettered` sync row is reopened by the player's next RSVP change; that is intentional.

## Test design

- Application: handlers (imported vs app-only, no `PickupPalUserId`, failure -> Pending + outbox,
  GameFull/GameNotFound mapping, refresh after success, promoted player synced on cancel);
  `RsvpPickupPalSyncService` with mocked ports; `PickupPalGameImportService` via the existing import
  tests; outbox handlers.
- Infrastructure: `PickupPalGamesClient` add/remove/get, API key header, both error shapes, no
  logger / no handlers guard. Repository methods are covered by the LocalDB suite (CI).
- Functions: `OutboxProcessor` claim/retry/dead-letter with a mocked repository; timer metadata
  and `Outbox:Enabled`; RSVP endpoint metadata unchanged.
