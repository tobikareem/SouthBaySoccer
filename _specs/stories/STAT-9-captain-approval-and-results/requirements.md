# STAT-9 - Captain approval and team results

**Epic:** STAT - Stats / TEAM - Teams & Matches  
**Milestone:** M11 client first, then M8/M9 backend  
**Visual source:** the `postgame` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## User story

*As a* captain, *I want* to approve submitted goals and assists after the game and record the team
result, *so that* leaderboards and player recent form only use trusted match facts.

## Acceptance criteria

```gherkin
Scenario: Captain approves submitted goals and assists
  Given I am an assigned captain for the match
  And players have submitted goals or assists
  When I approve a submission
  Then the raw stat event is marked captain-approved with actor and timestamp
  And it becomes eligible for leaderboards

Scenario: Non-captain cannot approve match stats
  Given I am not an assigned captain or authorized GameAdmin
  When I attempt to approve a submitted stat
  Then the request is rejected server-side
  And no leaderboard-visible stat changes

Scenario: Result applies to all assigned teammates
  Given a locked TeamAssignment list exists
  When a captain or GameAdmin records Team Green as Win, Draw, or Loss
  Then the MatchResult stores the outcome for each MatchTeam
  And every assigned teammate on that MatchTeam receives that outcome in recent form
  And checked-in players who were not assigned to a team do not receive a match result

Scenario: Conflicting result or stat approvals require resolution
  Given another captain has submitted a different result or rejected a stat
  When I submit a conflicting approval
  Then the match enters a Needs Review state
  And a GameAdmin must resolve it with an audit note before publishing

Scenario: Multi-team rotations record per-opponent outcomes
  Given the session has three or four teams
  And a team can rotate through matches against the other teams
  When a captain or GameAdmin records each rotation result as Win, Draw, or Loss
  Then the app keeps stepper counters for wins, draws, and losses per team for that game day
  And the combined Win + Draw + Loss count cannot exceed team count minus 1
  And each recorded result is persisted to the match/result history
  And assigned teammates receive the corresponding recent-form outcomes from those persisted results

Scenario: Rotation counter validation follows team count
  Given a two-team session
  Then a team's combined Win + Draw + Loss counter cannot exceed 1
  Given a three-team session
  Then a team's combined Win + Draw + Loss counter cannot exceed 2
  Given a four-team session
  Then a team's combined Win + Draw + Loss counter cannot exceed 3

Scenario: Published stats are corrected only through audit
  Given match stats and result are published
  When a correction is needed
  Then a StatCorrection audit record is required
  And raw facts are amended through the correction path, never silently overwritten
```

## Notes

- This story tightens the current STAT-7 "captain confirm" screen into a post-game workflow with
  explicit captain authority, conflict handling, and team result propagation.
- Recent form is derived from `MatchResult` and `TeamAssignment`, not manually edited profile state.
