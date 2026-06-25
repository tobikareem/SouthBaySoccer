# TEAM-4 - Captain assignment and draft design

## Screen contract

Wireframe screens:

- `captains` - GameAdmin assigns 2, 3, or 4 captains.
- `draft` - assigned captains pick players into their team.

Captain assignment layout:

1. Header with session name and `Admin only` status.
2. Segmented control for `2 captains` / `3 captains` / `4 captains`.
3. Captain slots showing selected captains.
4. Search field plus selectable checked-in roster list; captains cannot be selected from non-checked-in players.
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
| `TeamDraft.PickPlayer` | assigned Captain | Session or Match |
| `TeamDraft.Lock` | GameAdmin/Admin/Owner | Session or Match |

Captain status for a session is not a global role promotion. Tokens may expose role claims for UI,
but every draft command is authorized against the session/match resource on the server.

## Data rules

- Captains must be selected from checked-in players only.
- The captain checkbox limit equals the selected captain/team count: 2, 3, or 4.
- A player can have at most one active `TeamAssignment` per Match.
- Locking teams freezes the roster used by stat approval, ratings, and result propagation.
- All captain assignment, pick, unpick, and lock operations are audited.
