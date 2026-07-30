---
name: last-game-player-follow-up
description: Game Day last-game summary exposes player rating follow-up and approved goal/assist icon tallies
type: project
created: 2026-07-30
---

When no game is relevant today, the Game Day last-game summary carries the primary `MatchId` and a
server-projected `CanRateTeammates` capability. Eligible confirmed-roster players see “Finish up
this game” → “Rate teammates,” which reuses the existing `rate-teammates` route. The capability uses
the same `PeerFeedbackWindow` and roster rules as the rating endpoint.

Last-game team popup rows show approved totals compactly: one `⚽` per goal and one `🦶` per assist,
with `Captain ·` prefixed for captains. Screen readers receive the equivalent textual tally rather
than relying on emoji pronunciation.
