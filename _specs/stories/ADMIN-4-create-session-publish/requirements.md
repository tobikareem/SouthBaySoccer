# ADMIN-4 - Create and publish session

**Epic:** ADMIN - Admin & Live Game / SES - Seasons, Venues & Sessions  
**Milestone:** M11 client first, then M6 backend  
**Visual source:** the `adminsession` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## User story

*As an* Admin or GameAdmin, *I want* to create or update a pickup session with game date, time,
location, capacity, and team format, then publish it to the team, *so that* players can RSVP from the
Sessions screen and admins can correct session details later.

## Acceptance criteria

```gherkin
Scenario: Admin creates a game session draft
  Given I have CanManageSessions
  When I enter a game date, start time, check-in window, venue, format, and capacity
  Then a session draft is created with audit fields
  And it is not visible to players until published

Scenario: Admin publishes the session to the team
  Given a valid session draft exists
  When I publish the session
  Then the session appears in the team Sessions feed
  And eligible players can RSVP Going or join the waitlist
  And a notification or feed update is queued for the team

Scenario: Admin updates an existing created session
  Given I have CanManageSessions
  And a created or published session exists
  When I open it from the Created sessions list and save changed date, time, venue, format, or capacity
  Then the existing player-facing session is updated
  And no duplicate session is created
  And the session remains open for future updates

Scenario: Admin cancels a session without removing it
  Given I have CanManageSessions
  And a created or published session exists
  When I cancel or disable that session
  Then the session status is changed to Canceled
  And the session remains visible in the admin list and player Sessions feed
  And the session card and detail screen show "Session has been cancelled"
  And players cannot RSVP to the canceled session

Scenario: Admin deletes a session
  Given I have CanManageSessions
  And a created, published, or canceled session exists
  When I delete that session
  Then the session is soft-deleted with its audit history preserved
  And it no longer appears in the admin list or player Sessions feed
  And requesting it by id returns not found

Scenario: Required fields are validated
  Given I am creating a session
  When game date, start time, location, capacity, or format is missing
  Then publishing is blocked
  And the screen shows the missing fields without creating a player-visible session

Scenario: Capacity and time values are valid
  Given I am creating a session
  When capacity is less than 1 or check-in close is before check-in open
  Then the request is rejected
  And no session is published

Scenario: Unauthorized user cannot create a session
  Given I do not have CanManageSessions
  When I attempt to open or submit session creation
  Then access is denied server-side
  And the client does not rely on hidden buttons as authority
```

## Notes

- Publishing creates the player-facing session card used by `SES-6` and `RSVP-8`.
- Created sessions are listed on the admin screen and can be reopened for updates; updates modify the
  existing session rather than creating another one.
- Cancel/disable is reversible in persistence terms and keeps the session visible with a cancellation
  placard. Delete is a soft delete and removes the session from all ordinary queries.
- Check-in defaults can be generated from start time, but admins can adjust them before publish.
- The backend stores UTC timestamps; the UI displays venue-local date/time.
