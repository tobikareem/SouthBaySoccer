---
name: m14-pickuppal-roster-sync
description: M14 / RSVP-9 - in-app RSVPs on Pickup Pal-imported sessions are pushed to the Pickup Pal roster (add/remove), refreshed through the import path, and retried from the outbox by a timer; waiver gate removed from RSVP eligibility
type: project
created: 2026-09-16
---

Spec: `_specs/stories/RSVP-9-pickup-pal-roster-sync/`. Product decision (2026-09-16): an RSVP made
in the app for a game imported from WhatsApp adds the player to that game's roster on Pickup Pal,
and a cancel removes them. **Pickup Pal stays the source of truth for imported games; our database
is written first and is never rolled back because Pickup Pal failed.**

## Sync rules

- **Scope:** only sessions whose `OccurrenceKey` starts with `pickuppal:` (`PickupPalOccurrenceKey.TryGetGameId`)
  and only profiles with a `PickupPalUserId`. Anything else is `NotApplicable` and touches nothing.
- **Order:** the existing serializable RSVP transaction commits first (unchanged); then
  `IRsvpPickupPalSyncService.SyncAfterLocalWriteAsync(sessionId, playerProfileId)` runs from
  `SubmitRsvp` / `CancelRsvp` (also for the promoted player) / `AdminOverrideRsvp`. It never throws
  into the handler; any failure is reported as `Pending`.
- **Desired state is derived, never carried:** Going/Waitlisted locally => `POST api/games/{gameId}/players { playerId, playerName }`
  (never `playerNumber`); Maybe/NotGoing/no RSVP => `DELETE api/games/{gameId}/players/{playerId}`.
  Both the immediate push and the outbox retry read the *current* local state under the in-process
  `RsvpPickupPalSyncGate` (per session+player, per Function instance), so the last local write wins.
- **Write before call:** an outbox row `RsvpPickupPalSyncRequested` (idempotency key
  `RsvpPickupPalSyncRequested:{sessionId}:{playerProfileId}:{Add|Remove}`) and
  `RsvpResponse.PickupPalSyncStatus = Pending` are saved before Pickup Pal is called. Success ->
  `Synced` (+ `PickupPalSyncedAtUtc`) and row `Processed`; retryable failure -> `Pending` (+ safe
  code) and row `RetryScheduled` (+1 min); terminal rejection -> `Failed` (+ code) and row
  `Processed`. An existing row (even `DeadLettered`) is reopened by a new RSVP change unless the
  processor currently holds its lock.
- **Writer races:** `OutboxMessages` now carries a SQL `RowVersion`. A concurrency conflict on
  either save in the immediate path means the processor/another instance owns the row: the
  service calls `IUnitOfWork.DiscardChanges()`, re-tracks only the RSVP row, and still pushes. The
  processor's settle that loses the race is skipped. Any other persistence failure in the immediate
  path -> discard, best-effort `Failed(Unexpected)` on the RSVP row, and `Failed` is reported (not
  `Pending`, because there may be no outbox row to retry from).
- **Refresh after success:** `GET api/games/{gameId}` is upserted through
  `IPickupPalGameImportService.UpsertAsync([game])` — the same code the active-games import uses
  (`ImportPickupPalGamesCommandHandler` is now only fetch + upsert + save) — so Pickup Pal's own
  waitlist/capacity decision lands in the snapshot/participants and the attendance projection.
- `RsvpResponse` sync columns: `PickupPalSyncStatus` (`NotApplicable|Synced|Pending|Failed`),
  `PickupPalSyncedAtUtc`, `PickupPalSyncError` (codes `GameFull`, `GameNotFound`, `Rejected`,
  `Unavailable`, `Unexpected`; never a payload). `IRsvpRepository.FindRsvpForPickupPalSyncAsync`
  reads **including soft-deleted rows** because cancel soft-deletes the row. Migration
  `AddRsvpPickupPalSync` (controlled deploy). `RsvpResponseDto.PickupPalSync` is additive
  (default `"NotApplicable"`); the MAUI client is untouched.

## Matched Pickup Pal error strings (assumptions until Pickup Pal documents the Games errors)

`PickupPalGamesClient` parses both shapes (`{ "error": "text" }` / `{ "error": { "message" } }`),
case-insensitive substring:

| Call | Match | Result |
|---|---|---|
| add | `already` | `AlreadyApplied` -> Synced |
| add | `full` | `GameFull` -> Failed(GameFull), snapshot still refreshed |
| add | `player`/`user`/`participant` + `not found` | `Rejected` (unknown user id) |
| add | 404, or `game` + `not found` | `GameNotFound` -> Failed(GameNotFound), no refresh |
| remove | `not in`, `not a participant`, `not on`, or `player`/`user`/`participant` + `not found` (checked first) | `AlreadyApplied` |
| remove | `game` + `not found` | `GameNotFound` |
| remove | any other 404 | `AlreadyApplied` |
| any | 401/403/5xx/timeout/network | `ApplicationServiceUnavailableException` (retryable) |

Open (M14.7): confirm these strings, whether `POST players` auto-waitlists a full game, and that
API-added participants carry `userId` in `GET api/games/{id}`.

## Outbox processor

`OutboxFunctions.ProcessOutbox` — `[TimerTrigger("0 */5 * * * *")]` (constant; a missing
`%setting%` breaks host indexing), `Outbox:Enabled` (default true), `Outbox:BatchSize` 50,
`Outbox:LockDuration` 5 min, `Outbox:MaxAttempts` 6. Claimed types come from the static
`OutboxMessageTypes.Handled` list (keep it in sync with the handler registrations; a test checks).
`IOutboxMessageRepository.ClaimDueAsync` does a conditional `UPDATE` per candidate
(`Pending`/`RetryScheduled` and due, or `Processing` with an expired lock) setting
`LockToken`/`LockedUntilUtc`, then re-reads by `LockToken` + `Processing` alone (retry-strategy
safe), so concurrent instances never share a row. A handler that throws is settled in a fresh DI
scope. **HttpClient logging:** every Pickup Pal `AddHttpClient` chain calls `RemoveAllLoggers()`
and `host.json` sets `System.Net.Http.HttpClient` to Warning, so the factory never logs request
URIs; a test walks the built handler pipeline for all four clients.
Handlers (`IOutboxMessageHandler`, Application): `RsvpPickupPalSyncRequested` -> re-derive + push +
refresh; `PickupPalUserDeletionRequested` -> `DeleteUserAsync` (404 = done). Backoff 1, 5, 15, 60,
60... minutes; dead-letter at 6 attempts with `MaxAttemptsExceeded:{code}`; `Fail` dead-letters at
once; unknown types dead-letter `UnknownMessageType`. Each row settles in its own DI scope.
`PlayerRegistrationExternalFailed` and `PlayerWaitlistPromoted` rows are *not* drained (no handler).

## Waiver gate removed (same decision date)

There is no waiver requirement any more: `PlayerSessionEligibilityService` is the payment verdict
only (no `IWaiverRepository`), so a player with no waiver on file can RSVP, be promoted, and check
in. The Compliance entities, `waivers/*` endpoints, and migration history stay (dormant, not
deleted). The MAUI waiver copy is being removed separately.

## Deliberately out of scope

`bump-player` / `promote-player` / guest endpoints; creating Pickup Pal games from app-created
sessions; pushing lineups, check-ins, or stats; cross-instance ordering of two concurrent pushes
for one player (in-process gate only; the next RSVP change or retry repairs it); purge of
`OutboxMessages`.

Related: [[pickuppal-games-import]], [[m7-rsvp-waitlist]], [[session-feed-attendance-projection]],
[[m13-onboarding-backend]], [[m1-operational-records]], [[controlled-migrations]]
