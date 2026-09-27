# GDAY-1 - Game-day check-in tab design

## Screen contract

The authenticated Shell adds a Game Day tab for the active match-day workflow. The wireframe screen
is `gameday`.

Layout:

1. Header row: current session, date, and status pill (`Open`, `Checked in`, `Closed`).
2. Time card: game starts 7:40 PM, check-in 7:30 PM-7:45 PM, closes countdown.
3. Primary action:
   - `Check in at field` when eligible and open;
   - `Checked in` after success;
   - disabled closed/blocked copy after 7:45 PM or ineligible state.
4. Roster summary: Going, checked in, late/override count.
5. Roster list: every confirmed Going and Waitlist player with a checked-in indicator. For a caller
   holding `CanCheckInPlayers` (`canManageCheckIns`), each not-yet-checked-in row shows an admin
   **Check in** button that posts an in-window `CheckedIn` (the audited late-override path still handles
   arrivals after the window closes). Duplicate check-ins are no-ops (idempotency key + existing-row
   short-circuit + DB unique index), so a repeat tap is safe.
6. Role-aware shortcuts:
   - GameAdmin: Assign captains, and draft on behalf of any captain's team (the draft screen shows a
     team switcher when `canManageAllTeams`).
   - Captain: Pick team.
   - Captain/GameAdmin after game: Approve stats.

## State and services

`GameDayPageModel` depends on a future `IGameDayClient`.

```text
GameDayPageModel
  -> IGameDayClient.GetTodayContextAsync() // GET game-day/today
  -> IGameDayClient.CheckInAsync(sessionId, idempotencyKey)
  -> IGameDayClient.LateCheckInAsync(sessionId, playerProfileId, reason, idempotencyKey)
  -> IGameDayNavigator.OpenCaptainAssignment()
  -> IGameDayNavigator.OpenTeamDraft()
  -> IGameDayNavigator.OpenPostGameApproval()
```

The seed implementation stores check-in state in the same resettable seed state used by RSVP/stat
commands. Before the backend query runs, a Function-layer refresh coordinator invokes the same
Pickup Pal import used by Create Session in a separate dependency-injection scope. Refreshes are
single-flight, limited to five seconds, and throttled to once per minute so concurrent tab opens do
not reuse a canceled EF context or hammer Pickup Pal. The query then selects a published session
whose start is within the venue-local calendar day, preferring a same-day session where the current
player has a confirmed local or imported spot. It returns stored UTC start/check-in timestamps,
roster/check-in counts, current-player eligibility, and resource-scoped action permissions. The
client formats timestamps in the venue timezone but never recomputes eligibility from device time.

Pickup Pal games use the product Game Day window: ten minutes before kickoff through five minutes
after kickoff. Thus a 7:40 PM game supports ordinary self check-in from 7:30 PM through 7:45 PM.

Self check-in uses an authenticated-player endpoint that derives the target profile from the JWT;
the client cannot check in another player. Admin late check-in continues through the audited
`CanCheckInPlayers` path and requires a non-empty reason outside the stored window.

Captain assignment and team draft are wired end-to-end: the server computes `canAssignCaptains` and
`canDraftTeam` from policy, match phase, and captaincy (a GameAdmin also gets `canDraftTeam` so they
can draft on behalf of captains), and `ApiGameDayClient` calls the matching Game Day projections. The
post-game approval shortcut stays server-gated and its client method is still stubbed, so it remains
inert in API context until STAT-9 is wired.

## Edge cases

- No session today: show the empty Game Day state; future sessions remain in Sessions.
- Waitlisted or ineligible player: explain the block and link to session detail/payment/waiver.
- Offline: show the offline state; do not create a local authoritative check-in.
- Late arrival: GameAdmin override requires reason and writes audit data.
