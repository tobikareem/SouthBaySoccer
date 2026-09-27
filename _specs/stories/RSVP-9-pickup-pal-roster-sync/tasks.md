# RSVP-9 - Pickup Pal roster sync - Tasks

RSVP-9 is milestone **M14** (roadmap: [`../../tasks.md`](../../tasks.md)). Backend only; the MAUI
client is untouched (the new `pickupPalSync` field is additive and ignored by the current client).

- [x] **M14.1** Domain: `PickupPalSyncStatus` enum; `RsvpResponse` sync columns;
  `RsvpMutationResult.PickupPalSyncStatus`; `IRsvpRepository.FindRsvpForPickupPalSyncAsync` /
  `UpdateRsvp`; `IOutboxMessageRepository.ClaimDueAsync`.
  — Stories: `RSVP-9` · Projects: Domain · Depends on: nothing.

- [x] **M14.2** Application: extract `PickupPalGameImportService` from
  `ImportPickupPalGamesCommandHandler` (no behaviour change); extend `IPickupPalGamesClient` with
  `GetGameAsync` / `AddPlayerAsync` / `RemovePlayerAsync`; add `RsvpPickupPalSyncService`,
  `RsvpPickupPalSyncGate`, `RsvpOutboxMessages`, and the two `IOutboxMessageHandler`s; call the sync
  from Submit / Cancel / AdminOverride after the local transaction; add `PickupPalSync` to
  `RsvpResultModel`.
  — Stories: `RSVP-9` · Projects: Application · Depends on: M14.1.

- [x] **M14.3** Infrastructure: `PickupPalGamesClient` add/remove/get with API key header and both
  error shapes; `RsvpRepository` sync read/update and `GetMyRsvpAsync` carrying the status;
  `OutboxMessageRepository.ClaimDueAsync`; EF configuration; migration `AddRsvpPickupPalSync`.
  — Stories: `RSVP-9` · Projects: Infrastructure · Depends on: M14.1.

- [x] **M14.4** Functions + Contracts: `RsvpResponseDto.PickupPalSync`; `OutboxFunctions.ProcessOutbox`
  TimerTrigger (5 min), `OutboxProcessor`, `OutboxOptions` (`Outbox:Enabled` default true); DI
  registrations; `local.settings.json.example` keys.
  — Stories: `RSVP-9` · Projects: Functions, Contracts · Depends on: M14.2, M14.3.

- [x] **M14.5** Tests per `design.md` "Test design" in Application, Infrastructure (non-LocalDB),
  and Functions test projects; Client tests still green without edits.
  — Stories: `RSVP-9` · Depends on: M14.2–M14.4.

- [x] **M14.6** Knowledge base: `.ai/memory/m14-pickuppal-roster-sync.md` + INDEX line; story index
  row in `stories/README.md`; M14 section in `tasks.md`.
  — Stories: `RSVP-9` · Depends on: M14.5.

- [ ] **M14.7** External follow-ups: confirm with the Pickup Pal developer the Games error strings
  (`full`, `already`, `not found` variants) and whether `POST players` auto-waitlists when the game
  is full; confirm API-added participants carry `userId` in `GET api/games/{id}`. Correct the
  mapping table in `design.md` and the memory if they differ.
  — Stories: `RSVP-9` · Depends on: M14.4.

- [ ] **M14.8** Deploy: run migration `AddRsvpPickupPalSync` through the release pipeline; set
  `PickupPal:ApiKey` when Pickup Pal issues one; verify the timer runs on the Function App
  (`Outbox:Enabled`).
  — Stories: `RSVP-9` · Depends on: M14.4.

**Done when:** an RSVP in the app on an imported session shows the player on the Pickup Pal roster
(and a cancel removes them) within the request; a Pickup Pal outage leaves the local RSVP intact
with a pending outbox row that the timer drains; app-only sessions and profiles without a Pickup
Pal id are unaffected; no Pickup Pal URI or payload appears in logs; every scenario in
`requirements.md` has a passing test.
