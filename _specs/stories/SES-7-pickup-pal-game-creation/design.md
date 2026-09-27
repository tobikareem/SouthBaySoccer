# SES-7 - Pickup Pal game creation - Design

Realizes [`requirements.md`](requirements.md). The admin session workflow is described in
[`../ADMIN-4-create-session-publish/design.md`](../ADMIN-4-create-session-publish/design.md); the
roster sync, outbox processor, and single-game refresh this story reuses are in
[`../RSVP-9-pickup-pal-roster-sync/design.md`](../RSVP-9-pickup-pal-roster-sync/design.md).
Distilled rules for agents: `.ai/memory/m15-pickuppal-game-creation.md`.

## Principles

1. **Local first, never rolled back.** Every admin handler (create, update, publish, cancel, delete)
   commits exactly as before, then calls `ISessionPickupPalSyncService.SyncAfterLocalWriteAsync`.
   The sync can only change the session's Pickup Pal columns, the outbox, and the game snapshot.
2. **Ownership by origin.** `Session.PickupPalOrigin` says who owns the game: `Imported` games are
   Pickup Pal's (never created, updated, or terminated from here; re-import overwrites the session
   as before); `CreatedByApp` games are ours (the app's fields flow to Pickup Pal; re-import only
   confirms identity and refreshes the roster); `None` sessions are app-only.
3. **Action is derived, never carried.** `SessionPickupPalSyncService.ResolveAction(session)`
   computes ensure-created / ensure-updated / ensure-terminated / none from the session as it is
   now. The outbox payload's action is informational; a retry re-derives, so the last local write
   wins and a cancel that lands while a create is pending simply terminates on the next pass.
4. **`PickupPalGameId` is the link.** The RSVP roster sync and the import resolve a session's game
   by the stored game id first; the `pickuppal:{gameId}` occurrence key is only mirrored onto a
   session that has no occurrence key of its own (a recurrence occurrence keeps the
   `{ruleId}:{start}` key `CreateSessionOccurrenceCommandHandler` dedupes on) and remains the
   fallback for rows imported before the column existed.

## Components

| Layer | Type | Responsibility |
|---|---|---|
| Domain | `PickupPalOrigin` enum; `Session.GroupChatId` (FK `GroupChats`), `PickupPalOrigin`, `PickupPalGameId`, `PickupPalSyncStatus`, `PickupPalSyncedAtUtc`, `PickupPalSyncError` | Group association and persisted game identity / sync state. Error is a safe code, never a payload. |
| Domain | `ISessionRepository.FindForPickupPalSyncAsync`, `ListByPickupPalGameIdsAsync`; `ListByOccurrenceKeysAsync` now ignores the soft-delete filter | Read **including soft-deleted rows**: delete must still terminate the game, and the import must recognise a deleted app-created session instead of importing its game again. |
| Domain | `IGroupChatRepository.ListByIdsAsync` | Batch group names for the managed-sessions list. |
| Application | `SessionGroupResolver` | Explicit `GroupChatId` must exist; otherwise the acting admin's only active `PlayerGroupLink` (exactly one) or null. Reads our database only. |
| Application | `IPickupPalGamesClient.CreateGameAsync / UpdateGameAsync / TerminateGameAsync`, `PickupPalGameCreateRequest`, `PickupPalGameUpdateRequest`, `PickupPalGameWriteResult`, `PickupPalGameCreateResult` | Port extension. Retryable failures throw `ApplicationServiceUnavailableException`; terminal outcomes are values. |
| Application | `ISessionPickupPalSyncService` / `SessionPickupPalSyncService`, `SessionPickupPalSyncGate` (singleton, per session), `SessionOutboxMessages`, `SessionPickupPalSyncErrorCodes` | Immediate path and the shared core the outbox handler uses. |
| Application | `SessionAdminTimeZone.Resolve / ToLocal(utc, zone) / DefaultTimeZoneId` | Group time zone (`GroupChat.Timezone`, default `America/Los_Angeles`) for the date/time Pickup Pal expects. |
| Application | `PickupPalGameImportService` (changed) | Prefetches sessions by snapshot, occurrence key, and stored game id. Sets `Imported` + game id on imported sessions; on a `CreatedByApp` session only confirms the game id (and the occurrence key when the session has none), writes the session only when that changed, refreshes snapshot and participants, and resolves no venue from the game's location. A deleted `CreatedByApp` match skips the game (its termination is pending); a deleted imported match gets a fresh session as before. |
| Application | `SessionPickupPalSyncOutboxHandler` (`OutboxMessageTypes.Handled` updated) | Reads `SessionId` and `ActingPlayerProfileId`, calls `PushCurrentStateAsync`. |
| Infrastructure | `PickupPalGamesClient` (extended), `SessionRepository`, `GroupChatRepository`, EF configuration, migration `AddSessionPickupPalGame` | HTTP + EF implementations. The client keeps the URI-logging ban and sends `X-Api-Key` when configured. |
| Functions | DI registrations; `SchedulingFunctions` maps `groupChatId` in and `groupChatId` / `groupName` out | Composition only. |
| Contracts | `CreateSessionCommand.GroupChatId`, `CreateSessionAdminRequest.GroupChatId`, `SessionAdminResponse.GroupChatId`, `ManagedSessionDto.GroupChatId/GroupName`, `ManagedSessionEditDto.GroupName` | Additive trailing optional members; the MAUI client is untouched. |

## Field mapping (`POST api/games`)

| Pickup Pal field | Source | Notes |
|---|---|---|
| `gameType` | constant `WHATSAPP_GROUP` | app-only sessions are never sent |
| `groupId` | `GroupChat.ExternalId` | personal data: never logged |
| `date` / `time` | `Session.StartsAtUtc` in the group zone, `yyyy-MM-dd` / `HH:mm:ss` | zone = `GroupChat.Timezone` or `America/Los_Angeles`; unknown zone ids fall back to Pacific for formatting |
| `location` | `Venue.Name` + `", " + Venue.Address` when present | title if the venue row is gone |
| `maxPlayers` | `Session.Capacity` | |
| `creatorId` | acting admin's `PlayerProfile.PickupPalUserId` | missing -> terminal `MissingCreator` |
| `sport` | constant `SOCCER` | |
| `timezone` | the IANA zone id above | |
| `lat` / `lng` | omitted | sessions carry no coordinates |

`PUT api/games/{gameId}` sends `location` and `maxPlayers` (the contract example), plus `date`,
`time`, and `timezone` when `Session.StartsAtUtc` differs from the last snapshot's `StartsAtUtc`
(or there is no snapshot). Pickup Pal may ignore the extra fields; the next refresh shows what it
applied. `DELETE api/games/{gameId}` terminates.

Create response: the id is read from `id` or `gameId`, at the root or inside a `{ "game": {...} }`
envelope. A 2xx without a readable id is terminal `InvalidResponse` (a retry could create a
duplicate game).

## Flow - immediate path

```text
handler commits the local write (unchanged)
  -> SyncAfterLocalWriteAsync(sessionId)
       acquire SessionPickupPalSyncGate(sessionId)      (create is not idempotent upstream)
       session = FindForPickupPalSyncAsync (incl. deleted)
       action = ResolveAction(session); None -> NotApplicable (no writes)
       acting admin = ICurrentUser -> profile id (recorded in the payload for retries)
       write before call:
         outbox row SessionPickupPalSyncRequested (idempotency key
           "SessionPickupPalSyncRequested:{sessionId}:{EnsureCreated|EnsureUpdated|EnsureTerminated}",
           created or reopened - even when the processor currently holds it)
         session.PickupPalSyncStatus = Pending; SaveChanges
         a row-version conflict here means another writer owns the push: the session and the
           outbox row are re-read, the row is reopened again, Pending is saved and returned, so
           the latest local write always has a retry row
         the processor holds the row -> return Pending without pushing (its settle will lose
           the row-version race and the next run pushes the current state)
       Execute (below) -> outcome + the live session instance (a create that lost a row-version
         race while storing the game id re-reads the session; every later write targets that one)
       apply outcome to the session, settle the row (Processed, or RetryScheduled +1 min), SaveChanges
       any other exception: log the type only, DiscardChanges, best-effort Failed(Unexpected), return Failed
```

## Flow - execute (shared with the outbox handler)

```text
EnsureCreated   group ExternalId missing -> Failed(MissingGroup); creator id missing -> Failed(MissingCreator)
                CreateGameAsync -> Applied: persist PickupPalGameId, Origin=CreatedByApp, and
                  OccurrenceKey=pickuppal:{id} only when the session has no occurrence key (saved at
                  once: the refresh matches by game id in the database and a crash must not create
                  a second game), then best-effort GET api/games/{id} ->
                  PickupPalGameImportService.UpsertAsync -> Synced
                Rejected -> Failed(Rejected); InvalidResponse -> Failed(InvalidResponse)
EnsureUpdated   UpdateGameAsync(location, maxPlayers, start when changed vs snapshot, zone)
                Applied -> best-effort refresh -> Synced; Rejected -> Failed(Rejected)
                GameNotFound -> Failed(GameNotFound) and the link is dropped (PickupPalGameId null, a
                  mirrored pickuppal: occurrence key null, Origin stays CreatedByApp) so the next
                  admin write resolves to EnsureCreated and recreates the game
EnsureTerminated TerminateGameAsync -> Applied or GameNotFound -> Synced; Rejected -> Failed(Rejected)
any             ApplicationServiceUnavailableException (401/403/5xx/timeout/network) -> Pending(Unavailable) [retryable]
                other exception -> Pending(Unexpected) [retryable]
```

`PushCurrentStateAsync` (outbox path) takes the per-session lease **before** reading the session,
so a claim overlapping an in-flight create on the same instance sees the stored game id instead of
re-deriving `EnsureCreated` from a stale row.

Group rule: an update naming a different group while `PickupPalGameId` is set is rejected with a
conflict (the game lives in that WhatsApp group); omitting the group keeps it, so a group can never
be cleared.

`ResolveAction`: `Imported` -> None; deleted or `Canceled` -> `EnsureTerminated` when a game id
exists, else None; not `Published` -> None; game id exists -> `EnsureUpdated`; group set ->
`EnsureCreated`; otherwise None.

## Outbox

Same processor, options, backoff, and dead-letter rules as RSVP-9. Handler
`SessionPickupPalSyncOutboxHandler`: `Completed` for `Synced`, `NotApplicable`, and every `Failed`
(terminal outcomes live on the session; a new admin write reopens the row), `Retry(code)` for
`Pending`, `Fail(InvalidPayload)` for an unreadable payload.

## Persistence

`Sessions` gains `GroupChatId uniqueidentifier null` (FK `GroupChats`, index),
`PickupPalOrigin nvarchar(32) not null default 'None'`, `PickupPalGameId nvarchar(128) null`,
`PickupPalSyncStatus nvarchar(32) not null default 'NotApplicable'`,
`PickupPalSyncedAtUtc datetime2 null`, `PickupPalSyncError nvarchar(64) null`. Migration
`AddSessionPickupPalGame` backfills rows whose `OccurrenceKey` starts with `pickuppal:` to
`Imported` with the id from the key. Controlled deploy only.

## Logging

Unchanged from RSVP-9: no Pickup Pal client takes an `ILogger`, `RemoveAllLoggers()` stays on every
`AddHttpClient` chain, and the sync service logs exception type names and the action only.

## Limits (documented, accepted)

- The per-session gate is per Function instance. An admin write on one instance racing the outbox
  processor on another can, in a millisecond window, have the processor settle a row the admin
  path then fails to reopen; the next admin write or processor run reconciles. A cross-instance fix
  needs a distributed lock; deferred.
- The refresh after a create or update is best effort: the game exists, so a refresh failure leaves
  `Synced` and the next active-games import owns the snapshot.
- The update sends the start only when it differs from the last snapshot; if Pickup Pal ignores the
  start fields on `PUT`, the snapshot keeps the old start and every later update resends it.
- The MAUI Create Session page has no group picker yet; sessions rely on the single-group default.

## Test design

- Application: `SessionPickupPalSyncServiceTests` (create stores id and occurrence key, app-only,
  drafts, update with and without start, cancel and delete terminate, imported never pushed,
  unavailable -> Pending + outbox, missing creator / group terminal, rejected, refresh failure,
  processor-held row, outbox path, `ResolveAction` theory); `SessionGroupResolverTests` (0/1/2
  links, explicit, unauthenticated); `SessionAdminPickupPalHandlerTests` (commit-then-sync order,
  group defaulting and replacement); import tests for `Imported` marking and `CreatedByApp`
  ownership; outbox handler tests.
- Infrastructure: `PickupPalGamesClientTests` create/update/terminate payload shapes (date/time in
  the zone, no lat/lng, API key), envelope and id-name tolerance, missing id, 400/404/409 mapping,
  401/403/5xx/timeout retryable. `SchemaContractTests` for the new columns (LocalDB, CI only).
- Functions: handled-type list matches the registered handlers; scheduling endpoint metadata unchanged.
