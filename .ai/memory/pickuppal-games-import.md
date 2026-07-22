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
Past-start or zero-capacity games import as Draft with a logged warning.

**Why:** the WhatsApp groups on Pickup Pal are where games are actually organized; the app mirrors
them instead of competing with them.

**How to apply:**
- Persisted records: `PickupPalGameSnapshot` (sanitized JSON + key fields, unique per game id) and
  `PickupPalGameParticipant` (display name, guest + waitlist flags, join order) — **never persist
  or log `whatsappJid`, `groupId`, `subscriberId`, or raw payloads**; those embed phone numbers and
  the `PickupPalGamesClient` deliberately never deserializes them.
- `GET sessions/{id}/roster` unions local RSVP/waitlist profiles with imported participants
  (`GetSessionRosterQueryHandler`); the MAUI `ApiRosterClient` reads it.
- EF gotcha fixed here: never `OrderBy` after projecting into a positional record in an EF query —
  order on an anonymous projection first (see `RsvpRepository.ListGoingRosterAsync`).

Related: [[pickuppal-phone-sign-in]], [[m7-rsvp-waitlist]], [[controlled-migrations]]
