---
name: m15-pickuppal-game-creation
description: M15 / SES-7 - a session an admin publishes in the app with a WhatsApp group is created as a Pickup Pal game; the game id lives on the session and updates / cancellations propagate, retried from the outbox; imported games stay Pickup Pal's
type: project
created: 2026-09-16
---

Spec: `_specs/stories/SES-7-pickup-pal-game-creation/`. Product decisions (2026-09-16): app-created
sessions must also exist as games on Pickup Pal "as is"; the game identity is kept locally so later
app updates and cancellations propagate; Pickup Pal confirmed add-player on a full game waitlists
with a success response. **Local first, never rolled back** (same rule as RSVP-9).

## Ownership and scope

- `Session.PickupPalOrigin`: `None` (app-only), `Imported` (Pickup Pal owns it: the app never
  creates, updates, or terminates it; re-import overwrites the session as before), `CreatedByApp`
  (the app owns it: its fields flow to Pickup Pal; re-import only confirms `OccurrenceKey` /
  `PickupPalGameId`, refreshes snapshot + participants, and resolves **no venue** from the game's
  location because that string is our own "name, address").
- `Session.GroupChatId` (FK `GroupChats`) decides whether a session is pushed at all. Create /
  update commands take an optional `GroupChatId`; absent => the acting admin's only active
  `PlayerGroupLink` (exactly one) via `SessionGroupResolver`, else null (app-only). On update,
  absent keeps the existing association (the current MAUI client never sends it). Group membership
  is read from our database only (group writes to Pickup Pal remain forbidden).
- Action is **derived from current state** by `SessionPickupPalSyncService.ResolveAction`:
  `Imported` -> none; deleted or `Canceled` + game id -> ensure-terminated; not `Published` -> none
  (drafts never push); game id -> ensure-updated; group set -> ensure-created.
- **`PickupPalGameId` is the link.** After a successful create the session gets `PickupPalGameId`,
  `PickupPalOrigin = CreatedByApp`, and `OccurrenceKey = pickuppal:{gameId}` **only when it has no
  occurrence key** (a recurrence occurrence keeps its `{ruleId}:{start}` dedupe key), all **saved
  before the refresh** (the import matches by game id; a crash must not create a second game). The
  import prefetches by snapshot, occurrence key, and `ListByPickupPalGameIdsAsync`; the RSVP roster
  sync (M14) resolves the game by `PickupPalGameId` first and the `pickuppal:` key as fallback, so
  app-created games are covered too.
- Import reads by occurrence key / game id **ignore the soft-delete filter**: a deleted
  `CreatedByApp` match skips the game (its termination is pending) instead of resurrecting it; a
  deleted imported match gets a fresh session as before. `ApplyGameIdentity` writes the session only
  when the id or (null) key actually changed.
- Group rule: absent `GroupChatId` on update keeps the current group (a group can never be
  cleared); naming a different group once `PickupPalGameId` is set is an
  `ApplicationConflictException` ("The group cannot change once the Pickup Pal game exists.").
- `GameNotFound` on `PUT`: the link is dropped (`PickupPalGameId` null, a mirrored `pickuppal:`
  key null, origin stays `CreatedByApp`) so the next admin write resolves to ensure-created.

## Field mapping (`POST api/games`)

`gameType: "WHATSAPP_GROUP"`, `groupId: GroupChat.ExternalId`, `date: yyyy-MM-dd` and
`time: HH:mm:ss` of `StartsAtUtc` in the group zone (`GroupChat.Timezone`, default
`America/Los_Angeles`; `SessionAdminTimeZone.Resolve` falls back to Pacific for unknown ids),
`location: Venue.Name + ", " + Venue.Address` (address when present), `maxPlayers: Capacity`,
`creatorId: acting admin's PlayerProfile.PickupPalUserId`, `sport: "SOCCER"`, `timezone: the IANA
id`. **No `lat`/`lng`.** `PUT api/games/{gameId}` sends `location` + `maxPlayers`, plus
`date`/`time`/`timezone` only when the start differs from the last snapshot (Pickup Pal may ignore
them - the contract example shows location/maxPlayers only). `DELETE api/games/{gameId}`
terminates; 404 = already gone = success.

## Response / error assumptions (until Pickup Pal documents them - M15.8)

- Create id: `id` or `gameId`, at the root or inside `{ "game": {...} }`. A 2xx with no readable id
  is terminal `InvalidResponse` (retrying could duplicate the game).
- `PickupPalGamesClient` maps 5xx / 401 / 403 / timeout / network -> `ApplicationServiceUnavailableException`
  (retryable); 404 or a `game ... not found` message -> `GameNotFound`; any other 4xx (400 with a
  message included) -> `Rejected` (terminal; the message is never stored or logged).
- Terminal codes on `Sessions.PickupPalSyncError`: `MissingCreator` (admin without
  `PickupPalUserId`), `MissingGroup` (group without `ExternalId`), `Rejected`, `InvalidResponse`,
  `GameNotFound`; retryable: `Unavailable`, `Unexpected`.

## Outbox

`SessionPickupPalSyncRequested`, idempotency key
`SessionPickupPalSyncRequested:{sessionId}:{EnsureCreated|EnsureUpdated|EnsureTerminated}`, payload
`{ SessionId, Action, ActingPlayerProfileId, RequestedAtUtc }` (the admin id is needed for a
retried create's `creatorId`). Same processor / backoff / dead-letter as M14; `Failed` outcomes
complete the row (a new admin write reopens it). `SessionPickupPalSyncGate` (per session, per
instance) serializes pushes because create is not idempotent upstream; when the processor holds the
row, the immediate path reopens it and does **not** push (the processor's settle then loses the
row-version race and the next run pushes current state). `KeyedAsyncGate<TKey>` is the shared base
of both gates.

## Persistence

Migration `AddSessionPickupPalGame`: `Sessions.GroupChatId`, `PickupPalOrigin` (default `None`),
`PickupPalGameId`, `PickupPalSyncStatus` (default `NotApplicable`), `PickupPalSyncedAtUtc`,
`PickupPalSyncError`; backfills `pickuppal:*` occurrence keys to `Imported` + game id. Controlled
deploy only. `ISessionRepository.FindForPickupPalSyncAsync` reads including soft-deleted rows.

## Contracts (additive; MAUI untouched)

`CreateSessionCommand.GroupChatId`, `CreateSessionAdminRequest.GroupChatId`,
`SessionAdminResponse.GroupChatId`, `ManagedSessionDto.GroupChatId/GroupName`,
`ManagedSessionEditDto.GroupName` (+ `Command.GroupChatId`). The Create Session page has no group
picker yet (M15.7).

## Deliberately out of scope

Recurring games (`api/recurring-games`), guests, lineups, check-ins, bans, stats on Pickup Pal;
updating imported sessions from the app; cross-instance ordering of an admin write racing the
processor (documented limit). Concurrency rules: `PushCurrentStateAsync` takes the per-session lease
**before** reading the session; a create that loses a row-version race while storing the game id
re-reads the row and every later write targets the reloaded instance; a first-save conflict on the
immediate path re-reads and reopens the outbox row again so a retry row always exists.

Related: [[m14-pickuppal-roster-sync]], [[pickuppal-games-import]], [[m6-scheduling]],
[[pickuppal-groupchat-read-only]], [[controlled-migrations]]
