# GDAY-1 - Game-day check-in tab

**Epic:** CHK - Check-in / ADMIN - Live game  
**Milestone:** M11 client first, then M7 backend  
**Visual source:** the `gameday` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

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

Scenario: Player check-in records actual attendance
  Given the Game Day tab shows "Check in at field"
  When I submit check-in
  Then a CheckIn is recorded with the authoritative server timestamp
  And my RSVP remains Going
  And the screen changes to a checked-in state

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
```

## Notes

- The check-in clock is venue-local in the UI, but server time is authoritative and stored as UTC.
- RSVP remains attendance intent only (`INV-12`); check-in and no-show are separate attendance facts.
- The Game Day tab should be useful only near match day; outside that window it can show the next
  eligible session and disabled state rather than replacing the Sessions flow.
