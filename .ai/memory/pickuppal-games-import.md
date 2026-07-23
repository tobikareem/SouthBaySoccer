---
name: pickuppal-games-import
description: Pickup Pal active games import — source of truth for imported sessions, sanitized snapshots, roster union
type: project
created: 2026-07-22
---

Opening the admin Create session screen triggers a fail-open, time-boxed (5s) import of
`GET {PickupPal:BaseUrl}/api/games/active` (`ImportPickupPalGamesCommandHandler`, hooked into
`GetCreateSessionAdminDefaultsQueryHandler`). Each active game becomes/updates a `Session`
(occurrence key `pickuppal:{gameId}`; matched via snapshot → occurrence key; a merely coincidental
same start time is NOT adopted), with **Pickup Pal as source of truth on every re-import**
(capacity, times, title from group name + local weekday, auto-created venue from the location).
Active-feed games with capacity stay Published through kickoff so Game Day can discover them;
zero-capacity games import as Draft with a logged warning. Imported sessions use the product
check-in window of kickoff -10 minutes through kickoff +5 minutes.

**Why:** the WhatsApp groups on Pickup Pal are where games are actually organized; the app mirrors
them instead of competing with them.

**How to apply:**
- Persisted records: `PickupPalGameSnapshot` (sanitized JSON + key fields, unique per game id) and
  `PickupPalGameParticipant` (display name, guest + waitlist flags, join order) — **never persist
  or log `whatsappJid`, `groupId`, `subscriberId`, or raw phone numbers/payloads**. The
  `PickupPalGamesClient` is the only class that sees raw `phoneNumber`/`whatsappJid`; it emits only
  SHA-256 hashes (+ masked phone), normalized identically to `PickupPalUserSyncService` so hashes
  dedupe across sign-in and import.
- Import also upserts `PlayerProfile`s per participant (resolution order: `PickupPalUserId` →
  `PhoneNumberHash` → `WhatsAppJidHash`; no identity keys → snapshot-only, no profile), links
  `PickupPalGameParticipant.PlayerProfileId`, and backfills missing hash keys on matched profiles.
  Identity-linked (signed-in) profiles keep their own display name; import-owned ones follow
  Pickup Pal. First sign-in claims an unclaimed imported profile by phone hash
  (`PickupPalUserSyncService`), promoting Guest → Player.
- `GET sessions/{id}/roster` unions local RSVP/waitlist profiles with imported participants
  (`GetSessionRosterQueryHandler`), deduped by linked profile id (local entry wins); linked
  imported entries surface the real `PlayerProfileId` and can match `IsCurrentPlayer`. The MAUI
  `ApiRosterClient` reads it, and imported players appear in the Players tab via
  `ListDirectoryAsync`.
- `GET game-day/today` performs the import through an isolated, single-flight Function scope
  (5-second timeout, one-minute freshness throttle), then reads the persisted Pacific-calendar-day
  projection. This prevents a canceled import from contaminating the request DbContext.
- EF gotcha fixed here: never `OrderBy` after projecting into a positional record in an EF query —
  order on an anonymous projection first (see `RsvpRepository.ListGoingRosterAsync`).

Related: [[pickuppal-phone-sign-in]], [[m7-rsvp-waitlist]], [[controlled-migrations]]
