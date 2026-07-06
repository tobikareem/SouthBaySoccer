# ADMIN-4 - Create and publish session design

## Screen contract

The `adminsession` wireframe is an admin-only form reached from Sessions or Admin actions.

Layout:

1. Header: `Create session`, `Admin only`, back navigation.
2. Created sessions list showing existing sessions with title, date/time, format/capacity/status, and an `Edit` action.
3. Session basics card:
   - game date;
   - start time;
   - RSVP close/deadline;
   - check-in window preview.
4. Venue card:
   - searchable location field;
   - saved venue hint;
   - map/venue confirmation action.
5. Game setup card:
   - format selector, e.g. 5v5, 7v7, 9v9;
   - capacity stepper;
   - team count/captain mode selector for 2, 3, or 4 teams.
6. Publish preview card showing how the session will appear to players.
7. Primary action: `Publish to team` for a new session or `Update session` after an admin opens an existing session.

## State and services

```text
CreateSessionPageModel
  -> ISessionAdminClient.GetDefaultsAsync()
  -> ISessionAdminClient.ListManagedSessionsAsync()
  -> ISessionAdminClient.GetSessionForEditAsync(sessionId)
  -> ISessionAdminClient.SearchVenuesAsync(query)
  -> ISessionAdminClient.CreateDraftAsync(command)
  -> ISessionAdminClient.PublishAsync(sessionId)
  -> ISessionAdminClient.UpdateSessionAsync(sessionId, command)
  -> ISessionsNavigator.OpenSessionDetail(sessionId)
```

The UI-first seed implementation can create a draft in resettable seed state, list managed sessions,
load a session back into the form, update its session detail/feed card, and add newly published sessions
to the Sessions feed. The backend implementation must use `CanManageSessions`, FluentValidation,
server UTC timestamps, and audited create/publish/update commands.

## Validation

- Game date, start time, venue/location, format, and capacity are required.
- Capacity must be positive.
- Check-in open must be before check-in close.
- RSVP deadline cannot be after session start.
- Publishing is idempotent; duplicate taps should not create duplicate sessions.
- Updating an existing session must preserve the session id and should not create another feed card.