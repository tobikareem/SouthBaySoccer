# STAT-9 - Captain approval and results design

## Screen contract

The `postgame` wireframe is a captain/GameAdmin work queue after play.

Layout:

1. Header with match/session context and `Captain` or `Admin` authority badge.
2. Team result card with Team Green / Team White outcomes for two-team nights, or stepper rotation counters for three/four-team nights.
3. Pending approvals list for submitted goals and assists.
4. Conflict or Needs Review notice when captain submissions disagree.
5. Publish action that locks approved stats and result for leaderboard/profile reads.

## Workflow

```text
Player submits goals/assists
  -> Captain approves or rejects
  -> Captains/GameAdmin record team result
  -> Publish match
  -> Leaderboards derive from approved raw rows
  -> Player profile recent form derives from MatchResult + TeamAssignment
```

`STAT-7` remains the self-submit entry point. `STAT-9` is the authority workflow that decides which
submitted facts count.

## Data rules

- Goals and assists remain raw `MatchEvent` facts; approval adds review metadata.
- The result belongs to `MatchTeam`; player W/D/L recent form is derived from the player assignment
  to that match team. For three-team and four-team rotations, persist each team-vs-team rotation result
  and expose W/D/L steppers per team for the game day. Validate `wins + draws + losses <= teamCount - 1`, so two-team sessions max 1, three-team sessions max 2, and four-team sessions max 3.
- Conflicts move the match to Needs Review; GameAdmin resolution requires an audit note.
- Published stats can be changed only by `StatCorrection`.
