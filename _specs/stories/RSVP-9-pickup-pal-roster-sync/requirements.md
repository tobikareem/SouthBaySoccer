# RSVP-9 - Pickup Pal roster sync for imported games

**Epic:** RSVP - **Milestone:** M14 - **Backend + external (Pickup Pal) story**
**Applies:** `INV-11` (fail-closed), `NFR-Security` (no personal data in URLs or logs), the M7 RSVP
rules in [`../../requirements.md`](../../requirements.md) and `.ai/memory/m7-rsvp-waitlist.md`, the
import rules in `.ai/memory/pickuppal-games-import.md`, and the local-first persistence rule in
`.ai/memory/m13-onboarding-backend.md`.
**External contract:** the Games group of
[`documentation/pickuppal-api.postman.json`](../../../documentation/pickuppal-api.postman.json)
and the error shapes in
[`documentation/pickuppal-account-creation-api.md`](../../../documentation/pickuppal-account-creation-api.md) section 4.
**Builds on:** [`RSVP-8`](../RSVP-8-session-detail-screen/requirements.md) (RSVP toggle),
[`ADMIN-4`](../ADMIN-4-create-session-publish/requirements.md) (import of Pickup Pal active games).

## Context

Games organized in the WhatsApp groups are mirrored into N9ja Bay by the Pickup Pal active-games
import (`Session.OccurrenceKey = pickuppal:{gameId}`). Until now an RSVP made in the app was only
recorded locally: the player appeared on the app roster but never on the WhatsApp game, so the
organizer's `!!list` and the app disagreed. Product decision (2026-09-16): **when a player RSVPs in
the app for a game imported from WhatsApp, add them to that game's roster on Pickup Pal too, and
remove them when they cancel.** Pickup Pal stays the source of truth for imported games; our
database is written first and never rolled back because of an upstream failure.

## Story

*As a* player, *I want* my in-app RSVP for a WhatsApp game to show up on the Pickup Pal roster,
*so that* the organizer and the group see one list and I do not have to also type `!!in`.

*As an* organizer, *I want* app cancellations to free the spot on Pickup Pal, *so that* the bot's
waitlist promotion still works.

## Acceptance criteria

```gherkin
Scenario: Going on an imported session adds the player to the Pickup Pal roster
  Given a published session whose occurrence key starts with "pickuppal:"
  And my profile carries a Pickup Pal user id
  When I submit an RSVP of Going and the local capacity check confirms me
  Then my local RSVP is committed exactly as before
  And the Function App calls POST api/games/{gameId}/players with my Pickup Pal user id and display name
  And on success it re-reads GET api/games/{gameId} and updates the session's snapshot and participants through the import code path
  And the RSVP response reports pickupPalSync = "Synced"

Scenario: Waitlisted locally still pushes the intent to Pickup Pal
  Given the same imported session is full according to the combined attendance projection
  When I submit an RSVP of Going and am waitlisted locally
  Then the Function App still calls POST api/games/{gameId}/players so Pickup Pal can waitlist me on its side
  And the refreshed snapshot reflects whatever Pickup Pal decided

Scenario: Cancel, Not Going, or Maybe removes the player from the Pickup Pal roster
  Given I am Going on an imported session and on its Pickup Pal roster
  When I cancel my RSVP, or change it to NotGoing or Maybe
  Then my local state changes exactly as before (cancel promotes the next eligible waitlisted player)
  And the Function App calls DELETE api/games/{gameId}/players/{myPickupPalUserId}
  And a player promoted from the local waitlist by my cancellation is pushed to Pickup Pal as an add

Scenario: App-only sessions are untouched
  Given a published session whose occurrence key does not start with "pickuppal:"
  When I submit or cancel an RSVP
  Then no Pickup Pal call is made
  And the RSVP response reports pickupPalSync = "NotApplicable"

Scenario: Players without a Pickup Pal user id are not pushed
  Given an imported session
  And my profile has no Pickup Pal user id (guest or legacy profile)
  When I submit an RSVP
  Then the local RSVP succeeds
  And no Pickup Pal call is made
  And the RSVP response reports pickupPalSync = "NotApplicable"

Scenario: Admin override is synced the same way
  Given an imported session
  When an admin adds a player through the admin-override endpoint
  Then the local override is committed exactly as before
  And the player is pushed to Pickup Pal as an add when they carry a Pickup Pal user id

Scenario: Pickup Pal unavailable leaves the RSVP in place and queues a retry
  Given an imported session
  When the push to Pickup Pal times out, fails with a network error, or returns 401, 403, or 5xx
  Then the local RSVP stays committed
  And the RSVP row records pickupPalSyncStatus = Pending with a safe error code
  And an outbox row of type RsvpPickupPalSyncRequested is pending for the player and session
  And the RSVP response reports pickupPalSync = "Pending"

Scenario: Pickup Pal reports the game as full
  Given an imported session
  When POST api/games/{gameId}/players answers 4xx with an error that says the game is full
  Then the local RSVP stays committed
  And the RSVP row records pickupPalSyncStatus = Failed with error code GameFull
  And no retry is scheduled until the player changes their RSVP again

Scenario: Pickup Pal reports the player already on the roster
  Given an imported session
  When POST api/games/{gameId}/players answers 4xx with an error that says the player is already in the game
  Then the RSVP row records pickupPalSyncStatus = Synced

Scenario: Pickup Pal no longer has the game
  Given an imported session whose game Pickup Pal terminated
  When POST api/games/{gameId}/players answers 404
  Then the local RSVP stays committed
  And the RSVP row records pickupPalSyncStatus = Failed with error code GameNotFound
  And no retry is scheduled

Scenario: The outbox processor drains pending sync requests
  Given a pending RsvpPickupPalSyncRequested outbox row
  When the outbox timer runs
  Then it claims the row with a lock token and lock expiry
  And it re-derives the desired roster state from the player's current local RSVP (last write wins)
  And it pushes that state and refreshes the game snapshot
  And a success marks the row Processed, a retryable failure schedules it again with backoff 1, 5, 15, then 60 minutes
  And the sixth failed attempt dead-letters the row with a safe reason

Scenario: The outbox processor also drains Pickup Pal account deletions
  Given a pending PickupPalUserDeletionRequested outbox row
  When the outbox timer runs
  Then it calls DELETE api/users/{id} and marks the row Processed on success or on a 404

Scenario: Rapid opposite taps converge on the last local state
  Given I tap Going and then NotGoing within a second on an imported session
  When both requests land on the same Function instance
  Then the two pushes are serialized per player and session
  And each push sends the player's local state as it is at the moment the push starts

Scenario: Nothing personal is logged or placed in URLs
  Given any push, removal, or refresh call to Pickup Pal
  Then request URIs and bodies are never logged (the HttpClient factory's default request logging is removed for every Pickup Pal client)
  And the add request body carries only playerId and playerName (the profile display name, which Pickup Pal already holds) and never a phone number
  And the RSVP row and the outbox row store only ids, status codes, and safe error codes, never a Pickup Pal payload
```

## Out of scope (deliberate)

- `bump-player`, `promote-player`, and the guest endpoints. Pickup Pal decides waitlist order; we
  only add and remove the player themself.
- Creating Pickup Pal games from app-created sessions (app-only sessions stay app-only).
- Pushing lineups, check-ins, or stats to Pickup Pal.
- Cross-instance ordering of two concurrent pushes for the same player (see `design.md`, Limits).
- Retention or purge of `OutboxMessages` rows.
