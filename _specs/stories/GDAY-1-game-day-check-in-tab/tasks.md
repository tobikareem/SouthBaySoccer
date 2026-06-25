# GDAY-1 - Game-day check-in tab tasks

- [ ] **M11.GDAY1a** Add Game Day Shell tab and `GameDayPage`/`GameDayPageModel` matching the
  `gameday` wireframe, including open, checked-in, closed, ineligible, and no-session states.
- [ ] **M11.GDAY1b** Add `IGameDayClient` seed contract and stateful check-in command; ensure RSVP
  remains unchanged when check-in succeeds.
- [ ] **M11.GDAY1c** Add navigation shortcuts to captain assignment, captain draft, and post-game
  approval based on role/permission flags from the seed context.
- [ ] **M7.GDAY1d** Implement backend check-in use case using server UTC, venue-local window
  evaluation, idempotency key, and GameAdmin late override audit.
- [ ] **TEST.GDAY1e** Cover page-model states, command idempotency, disabled closed/ineligible
  states, and backend authorization/time-window tests.

**Done when:** the `gameday` wireframe is reproduced with shared controls, check-in is a separate
attendance fact from RSVP, and backend tests prove the 7:30 PM-7:45 PM window and override audit.
