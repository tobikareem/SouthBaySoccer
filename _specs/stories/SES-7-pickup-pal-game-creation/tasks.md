# SES-7 - Pickup Pal game creation - Tasks

SES-7 is milestone **M15** (roadmap: [`../../tasks.md`](../../tasks.md)). Backend only; the MAUI
client is untouched (the new `groupChatId` / `groupName` members are additive and ignored by the
current client).

- [x] **M15.1** Domain: `PickupPalOrigin`; `Session.GroupChatId` and the Pickup Pal game / sync
  columns; `ISessionRepository.FindForPickupPalSyncAsync`; `IGroupChatRepository.ListByIdsAsync`.
  — Stories: `SES-7` · Projects: Domain · Depends on: nothing.

- [x] **M15.2** Application: `SessionGroupResolver`; `IPickupPalGamesClient` create / update /
  terminate port; `SessionPickupPalSyncService`, `SessionPickupPalSyncGate`,
  `SessionOutboxMessages`, `SessionPickupPalSyncErrorCodes`; `SessionAdminTimeZone.Resolve`;
  `PickupPalGameImportService` ownership rule; `SessionPickupPalSyncOutboxHandler`; group on the
  create / update commands and models; sync call from create, update, publish, cancel, delete.
  — Stories: `SES-7` · Projects: Application · Depends on: M15.1.

- [x] **M15.3** Infrastructure: `PickupPalGamesClient` create / update / terminate with API key,
  envelope-tolerant id parsing, both error shapes; repository reads; EF configuration; migration
  `AddSessionPickupPalGame` with the occurrence-key backfill.
  — Stories: `SES-7` · Projects: Infrastructure · Depends on: M15.1.

- [x] **M15.4** Functions + Contracts: DI registrations (gate, resolver, service, handler);
  `groupChatId` on the create / update requests; `groupChatId` / `groupName` on the admin responses.
  — Stories: `SES-7` · Projects: Functions, Contracts · Depends on: M15.2, M15.3.

- [x] **M15.5** Tests per `design.md` "Test design" in Application, Infrastructure (non-LocalDB
  locally; schema contract in CI), and Functions; Client tests green without edits.
  — Stories: `SES-7` · Depends on: M15.2–M15.4.

- [x] **M15.6** Knowledge base: `.ai/memory/m15-pickuppal-game-creation.md` + INDEX line; story
  index row in `stories/README.md`; M15 section in `tasks.md`.
  — Stories: `SES-7` · Depends on: M15.5.

- [x] **M15.7** MAUI: group picker on the Create Session / edit page bound to `groupChatId`.
  Editing preserves the original group, including null, even if it is absent from the admin's
  own membership choices; a stale/default selection must not change an existing session's group.
  — Stories: `SES-7` · Projects: SouthBaySoccer · Depends on: M15.4.

- [ ] **M15.8** External follow-ups: confirm with the Pickup Pal developer the create response
  shape (`id` vs `gameId`, envelope) and whether `PUT api/games/{gameId}` honours `date` / `time` /
  `timezone`; correct `design.md` and the memory if they differ.
  — Stories: `SES-7` · Depends on: M15.3.

- [ ] **M15.9** Deploy: run migration `AddSessionPickupPalGame` through the release pipeline;
  verify a publish from the app appears in the group.
  — Stories: `SES-7` · Depends on: M15.4.

**Done when:** publishing a session with a group in the app creates the game in that WhatsApp
group, later updates and cancellations reach it, a Pickup Pal outage leaves the local session
intact with a pending outbox row the timer drains, app-only and imported sessions are untouched,
and the MAUI client keeps compiling and passing unchanged.
