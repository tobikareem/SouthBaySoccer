# TEAM-4 - Captain assignment and draft design

## Screen contract

Wireframe screens:

- `captains` - GameAdmin assigns 2, 3, or 4 captains.
- `draft` - assigned captains pick players into their team.

Captain assignment layout:

1. Header with session name and `Admin only` status.
2. Segmented control for `2 captains` / `3 captains` / `4 captains`.
3. Captain slots showing selected captains.
4. Search field plus selectable confirmed (Going + Waitlist) roster; a not-yet-checked-in confirmed player is still eligible to be a captain or drafted.
5. Sticky primary action `Grant captain permissions`.

Team draft layout:

1. Header with team name and captain identity.
2. Team count summary and unassigned player count.
3. Checkbox-style player rows from the confirmed game list.
4. Locked/assigned rows are disabled and show the owning team.
5. Primary action `Save team picks`.

## Authorization model

Team topology:

- `2 captains` creates two teams.
- `3 captains` creates three separate teams.
- `4 captains` creates four separate teams.
- Each captain owns one draft team for the session/match format; players cannot belong to more than
  one active team assignment for the same match.

Use resource-scoped actions rather than permanent role mutation:

| Action | Who can grant/use | Scope |
|---|---|---|
| `Session.Captains.Assign` | GameAdmin/Admin/Owner | Session |
| `TeamDraft.PickPlayer` | assigned Captain (own team), or GameAdmin/Admin/Owner (any team) | Session or Match |
| `TeamDraft.Lock` | GameAdmin/Admin/Owner | Session or Match |

Captain status for a session is not a global role promotion. Tokens may expose role claims for UI,
but every draft command is authorized against the session/match resource on the server.

## Data rules

- Captains and draft picks come from the confirmed **Going + Waitlist** roster (local RSVPs plus linked
  imported Pickup Pal participants). Check-in is a separate attendance fact and does **not** gate team
  eligibility: an admin can check players in from Game Day, but a not-yet-checked-in Going or Waitlist
  player is still eligible for a captaincy or a team.
- The captain checkbox limit equals the selected captain/team count: 2, 3, or 4.
- A player can have at most one active `TeamAssignment` per Match.
- Locking teams freezes the roster used by stat approval, ratings, and result propagation.
- All captain assignment, pick, unpick, and lock operations are audited.

## Server contract

- `GET /api/game-day/sessions/{sessionId}/captains` returns the confirmed (Going + Waitlist) roster
  and current captain topology. `PUT` on the same route replaces the desired 2-4 captain topology and
  requires an idempotency key.
- `GET /api/game-day/sessions/{sessionId}/draft` returns the caller's editable team, all teams and
  assignments, and `canManageAllTeams` (true for GameAdmin/Admin/Owner) so the client shows a team
  switcher. `PUT /api/game-day/sessions/{sessionId}/teams/{teamId}/picks` replaces that team's desired
  roster and requires an idempotency key.
- `POST /api/game-day/sessions/{sessionId}/teams/lock` is the GameAdmin-only, idempotent transition
  from `Draft` to `InProgress`. It validates that every team has its captain and that every drafted
  player is confirmed (Going or Waitlist), then writes an audit entry.
- Captain topology can be changed only while the primary match is `Draft` and no non-captain picks
  exist. A captain replaces picks only for the team they captain; a GameAdmin/Admin/Owner can draft on
  behalf of any captain's team (the acting profile id is recorded in the audit entry).
- Captain picks close when the match leaves `Draft` or the post-game window opens. Post-game writes
  still require the explicit `Draft` to `InProgress` lock transition. The unique active
  `(MatchId, PlayerProfileId)` assignment constraint remains the final guard against two captains
  selecting the same player concurrently.
