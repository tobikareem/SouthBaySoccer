# STAT-9 - Captain approval and results tasks

- [ ] **M11.STAT9a** Add `PostGameApprovalPage`/`PostGameApprovalPageModel` matching the `postgame`
  wireframe for captains/GameAdmins.
- [ ] **M11.STAT9b** Extend seed stats/game-day state with pending approvals, approve/reject actions,
  result selection, three/four-team rotation steppers with max-combined validation, conflict state, and published state.
- [x] **M8.STAT9c** Implement backend captain approval commands and result recording with scoped
  authorization and audit fields.
- [ ] **M9.STAT9d** Ensure profile recent form and leaderboard projections read from approved raw
  events and `MatchResult`/`TeamAssignment`, not mutable profile totals, following
  [`../../m9-leaderboards-queries.md`](../../m9-leaderboards-queries.md).
- [ ] **TEST.STAT9e** Cover non-captain denial, approval visibility, conflict-to-review transition,
  result propagation to assigned teammates, rotation W/D/L stepper validation, and no result for unassigned checked-in players.

**Done when:** only captains/GameAdmins can approve goals/assists, team results update every assigned
teammate's recent form through derived reads, and conflicts require audited admin resolution.
