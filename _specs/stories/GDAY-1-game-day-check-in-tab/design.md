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
5. Role-aware shortcuts:
   - GameAdmin: Assign captains.
   - Captain: Pick team.
   - Captain/GameAdmin after game: Approve stats.

## State and services

`GameDayPageModel` depends on a future `IGameDayClient`.

```text
GameDayPageModel
  -> IGameDayClient.GetTodayContextAsync()
  -> IGameDayClient.CheckInAsync(sessionId, idempotencyKey)
  -> IGameDayNavigator.OpenCaptainAssignment()
  -> IGameDayNavigator.OpenTeamDraft()
  -> IGameDayNavigator.OpenPostGameApproval()
```

The seed implementation stores check-in state in the same resettable seed state used by RSVP/stat
commands. The backend implementation authorizes and timestamps on the server; the client never trusts
device time for eligibility.

## Edge cases

- No session today: show the next session and route back to Sessions.
- Waitlisted or ineligible player: explain the block and link to session detail/payment/waiver.
- Offline: allow read-only cached context, but do not create a local authoritative check-in.
- Late arrival: GameAdmin override requires reason and writes audit data.
