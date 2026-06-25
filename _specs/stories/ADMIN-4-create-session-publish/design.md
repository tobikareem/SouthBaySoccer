# ADMIN-4 - Create and publish session design

## Screen contract

The `adminsession` wireframe is an admin-only form reached from Sessions or Admin actions.

Layout:

1. Header: `Create session`, `Admin only`, back navigation.
2. Session basics card:
   - game date;
   - start time;
   - RSVP close/deadline;
   - check-in window preview.
3. Venue card:
   - searchable location field;
   - saved venue hint;
   - map/venue confirmation action.
4. Game setup card:
   - format selector, e.g. 5v5, 7v7, 9v9;
   - capacity stepper;
   - team count/captain mode selector for 2, 3, or 4 teams.
5. Publish preview card showing how the session will appear to players.
6. Primary action: `Publish to team`.

## State and services

```text
CreateSessionPageModel
  -> ISessionAdminClient.GetDefaultsAsync()
  -> ISessionAdminClient.SearchVenuesAsync(query)
  -> ISessionAdminClient.CreateDraftAsync(command)
  -> ISessionAdminClient.PublishAsync(sessionId)
  -> ISessionsNavigator.OpenSessionDetail(sessionId)
```

The UI-first seed implementation can create a draft in resettable seed state and add the published
session to the Sessions feed. The backend implementation must use `CanManageSessions`, FluentValidation,
server UTC timestamps, and audited create/publish commands.

## Validation

- Game date, start time, venue/location, format, and capacity are required.
- Capacity must be positive.
- Check-in open must be before check-in close.
- RSVP deadline cannot be after session start.
- Publishing is idempotent; duplicate taps should not create duplicate sessions.
