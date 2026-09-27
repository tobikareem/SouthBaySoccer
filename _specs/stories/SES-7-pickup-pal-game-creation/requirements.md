# SES-7 - Pickup Pal game creation for app-published sessions

**Epic:** SES - **Milestone:** M15 - **Backend + external (Pickup Pal) story**
**Applies:** `INV-11` (fail-closed), `NFR-Security` (no personal data in URLs or logs), the M6
scheduling rules in [`../../requirements.md`](../../requirements.md) and `.ai/memory/m6-scheduling.md`,
the import rules in `.ai/memory/pickuppal-games-import.md`, the roster-sync and outbox rules in
`.ai/memory/m14-pickuppal-roster-sync.md`, and the group-chat read-only rule (our database owns
player-to-group linkage; group writes to Pickup Pal are forbidden).
**External contract:** the Games group of
[`documentation/pickuppal-api.postman.json`](../../../documentation/pickuppal-api.postman.json)
(`POST api/games`, `PUT api/games/{gameId}`, `DELETE api/games/{gameId}`, `GET api/games/{gameId}`).
**Builds on:** [`ADMIN-4`](../ADMIN-4-create-session-publish/requirements.md) (draft / publish
workflow), [`RSVP-9`](../RSVP-9-pickup-pal-roster-sync/requirements.md) (roster sync, outbox
processor, single-game refresh through the import path).

## Context

Until now a session an admin created in the app existed only in N9ja Bay: the WhatsApp group never
saw it, `!!list` had nothing to show, and app RSVPs on it could not reach Pickup Pal because the
roster sync only covers sessions whose occurrence key marks them as imported. Product decisions
(2026-09-16): **(1)** sessions created by an admin in the app must also be created as games on
Pickup Pal, using the fields Pickup Pal's contract needs, as is; **(2)** the created game's identity
is kept in our database so later updates and cancellations from the app propagate; **(3)** Pickup
Pal confirmed that adding a player to a full game places them on the waitlist with a success
response, so a 2xx on add-player is always `Synced`.

Only sessions that belong to a WhatsApp group are pushed; a session without a group stays app-only.
Pickup Pal keeps owning imported games: the app never creates or terminates those.

## Story

*As an* organizer, *I want* the session I publish in the app to appear as the game in our WhatsApp
group, *so that* the group RSVPs with `!!in` as usual and the app and the bot show one roster.

*As an* organizer, *I want* my later edits and cancellations in the app to reach the group's game,
*so that* I do not have to repeat them in WhatsApp.

## Acceptance criteria

```gherkin
Scenario: A draft carries the admin's group by default
  Given I am an admin linked to exactly one WhatsApp group
  When I create a session draft without naming a group
  Then the draft is associated with that group
  And no Pickup Pal call is made

Scenario: An admin with zero or several groups gets an app-only draft unless they name one
  Given I am an admin linked to no group, or to two or more groups
  When I create a session draft without naming a group
  Then the draft has no group and stays app-only
  When I create a session draft naming one of my groups
  Then the draft is associated with the named group

Scenario: Publishing a session with a group creates the Pickup Pal game
  Given a draft session associated with a WhatsApp group
  And I am an admin whose profile carries a Pickup Pal user id
  When I publish the session
  Then the local publish is committed exactly as before
  And the Function App calls POST api/games with gameType WHATSAPP_GROUP, the group's Pickup Pal id,
    the start as date (yyyy-MM-dd) and time (HH:mm:ss) in the group's time zone, location as the
    venue name plus ", address" when present, maxPlayers as the capacity, creatorId as my Pickup Pal
    user id, sport SOCCER, and timezone as the IANA zone (America/Los_Angeles when the group has none)
  And no latitude or longitude is sent
  And the created game id is stored on the session with origin CreatedByApp
  And the session's occurrence key becomes "pickuppal:{gameId}" only when it had none (a recurrence occurrence keeps its recurrence key)
  And the game is re-read through GET api/games/{gameId} and upserted through the import path
  And the session's Pickup Pal sync status is Synced

Scenario: Publishing a session without a group stays app-only
  Given a draft session with no group
  When I publish it
  Then it is published locally and no Pickup Pal call is made
  And the session's Pickup Pal sync status is NotApplicable

Scenario: Draft edits never push
  Given a draft session with a group
  When I update it
  Then no Pickup Pal call is made

Scenario: The group is fixed once the game exists
  Given a published session whose game the app created
  When I update it naming a different group
  Then the update is rejected with a conflict ("The group cannot change once the Pickup Pal game exists.")
  And omitting the group keeps the current one (a group can never be cleared)

Scenario: Pickup Pal no longer has the game on update
  Given a published session whose game the app created
  When PUT api/games/{gameId} answers 404
  Then the session records Pickup Pal sync status Failed with code GameNotFound
  And the game link is dropped (origin stays CreatedByApp) so the next admin write recreates the game

Scenario: Updating a published app-created session updates the game
  Given a published session whose game the app created
  When I update it
  Then the local update is committed exactly as before
  And the Function App calls PUT api/games/{gameId} with location and maxPlayers
  And it also sends date, time, and timezone when the start differs from what Pickup Pal last reported
  And the game is re-read and upserted through the import path

Scenario: Canceling or deleting an app-created session terminates the game
  Given a session whose game the app created
  When I cancel it or delete it
  Then the local change is committed exactly as before
  And the Function App calls DELETE api/games/{gameId}
  And a 404 (the game is already gone) counts as success

Scenario: Imported sessions are never created, updated, or terminated by the app
  Given a session imported from Pickup Pal (origin Imported)
  When I publish, update, cancel, or delete it
  Then the local change is applied and no Pickup Pal game call is made

Scenario: Re-import recognises an app-created game
  Given a session whose game the app created
  When the active-games import or the single-game refresh sees that game
  Then it adopts the existing session by its stored game id (or occurrence key) instead of creating a second one
  And it keeps origin CreatedByApp, the session's own occurrence key, and the admin's title, venue, capacity, times, and status
  And it still refreshes the game snapshot and participants
  And a deleted app-created session (its game is being terminated) is skipped rather than imported again

Scenario: Pickup Pal unavailable leaves the session in place and queues a retry
  Given a session that must be created, updated, or terminated on Pickup Pal
  When the call times out, fails with a network error, or answers 401, 403, or 5xx
  Then the local session change stays committed
  And the session records Pickup Pal sync status Pending with a safe error code
  And an outbox row of type SessionPickupPalSyncRequested is pending for the session
  And the outbox processor later re-derives the action from the session's current state and pushes it

Scenario: A missing creator or group id is terminal
  Given a session to create whose acting admin has no Pickup Pal user id, or whose group has no Pickup Pal id
  When the publish runs
  Then the session records Pickup Pal sync status Failed with code MissingCreator or MissingGroup
  And no retry is scheduled until an admin writes the session again

Scenario: Pickup Pal rejects the request
  Given a game create, update, or terminate
  When Pickup Pal answers 400 with a message
  Then the session records Pickup Pal sync status Failed with code Rejected
  And the message itself is never stored or logged

Scenario: Adding a player to a full game is a success
  Given an in-app RSVP on any Pickup Pal-linked session
  When POST api/games/{gameId}/players answers 2xx
  Then the RSVP row records pickupPalSync = Synced regardless of the response body
  And the refreshed snapshot shows whether Pickup Pal put the player on its waitlist

Scenario: Nothing personal is logged or placed in URLs
  Given any game create, update, terminate, or refresh call
  Then request URIs and bodies are never logged (the HttpClient factory's default request logging stays removed)
  And the session and outbox rows store only ids, status codes, and safe error codes

Scenario: Admin session responses carry the group
  Given any admin session response, managed-session row, or admin-edit payload
  Then it includes groupChatId and groupName (null for an app-only session)
  And the current MAUI client keeps compiling and behaving unchanged
```

## Out of scope (deliberate)

- Pickup Pal recurring games (`api/recurring-games`); each published session is one game.
- Guests, lineups, check-ins, bans, and stats on Pickup Pal.
- A group picker on the MAUI Create Session page (the backend accepts `groupChatId`; the client
  relies on the single-group default for now).
- Updating an imported session from the app (Pickup Pal owns it; the next import re-applies its state).
- Cross-instance ordering of an admin write racing the outbox processor (see `design.md`, Limits).
