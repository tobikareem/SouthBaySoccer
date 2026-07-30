# GDAY-1 - Game-day check-in tab

**Epic:** CHK - Check-in / ADMIN - Live game  
**Milestone:** M11 client first, then M7 backend  
**Visual source:** the `gameday` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

> **Superseded in part by [GDAY-2](../GDAY-2-relevant-games-and-spectator/requirements.md):** which
> of today's games the page shows (relevance filtering, the admin "All games today" toggle, the
> spectator view for group-chat games, and the no-game/last-game state) is specified there. The
> check-in mechanics in this story are unchanged.

## User story

*As a* player with a confirmed spot, *I want* a game-day tab where I can check in at the field between
7:30 PM and 7:45 PM, *so that* actual attendance is recorded separately from my RSVP intent.

## Acceptance criteria

```gherkin
Scenario: Game Day tab opens during the check-in window
  Given I have a confirmed Going RSVP for tonight's session
  And the venue local time is between 7:30 PM and 7:45 PM
  When I open the Game Day tab
  Then I see the current session, game start time 7:40 PM, and check-in close time 7:45 PM
  And the primary action says "Check in at field"

Scenario: Game Day refreshes from Pickup Pal before selecting today's session
  Given Pickup Pal has an active game whose venue-local date is today
  When an authenticated player opens the Game Day tab
  Then the backend reuses the sanitized active-games import pipeline
  And selects the imported published session for today's venue-local calendar date
  And the MAUI client never calls Pickup Pal directly

Scenario: Pickup Pal is temporarily unavailable
  Given a previously imported session exists for today
  And the Pickup Pal refresh fails or exceeds five seconds
  When I open the Game Day tab
  Then the backend fails open to the locally persisted session
  And no provider secrets or raw participant identifiers are returned

Scenario: Player check-in records actual attendance
  Given the Game Day tab shows "Check in at field"
  When I submit check-in
  Then a CheckIn is recorded with the authoritative server timestamp
  And my RSVP remains Going
  And the screen changes to a checked-in state
  And the self-check-in endpoint is authorized for the authenticated player
  And the server verifies that the player has a confirmed local or imported Going spot

Scenario: Check-in closes at 7:45 PM
  Given the venue local time is after 7:45 PM
  When I open the Game Day tab
  Then normal player check-in is disabled
  And the screen explains that a GameAdmin override is required

Scenario: Ineligible or waitlisted player cannot self-check in
  Given I am waitlisted, not Going, unpaid, or missing a current waiver
  When I open the Game Day tab
  Then the self-check-in action is unavailable
  And the reason is shown without consuming a roster spot

Scenario: Late check-in override is audited
  Given check-in has closed
  And a GameAdmin has CanCheckInPlayers
  When the GameAdmin records a late arrival with a reason
  Then a CheckIn is recorded with an override flag, reason, actor, and timestamp

Scenario: Game-day actions are resource and phase scoped
  Given the Game Day context is loaded
  Then action visibility comes from server-computed session permissions
  And an admin role alone does not force captain-only or post-game actions visible

Scenario: No session today
  Given there is no published session on today's venue-local calendar date
  When I open the Game Day tab
  Then I see "No session today"
  And no check-in or game-management action is available
```

## Notes

- The client displays venue-local labels, but eligibility and window enforcement use the session's
  stored UTC timestamps and server `IClock.UtcNow`.
- RSVP remains attendance intent only (`INV-12`); check-in and no-show are separate attendance facts.
- The Game Day tab should be useful only near match day; outside that window it can show the next
  eligible session and disabled state rather than replacing the Sessions flow.
