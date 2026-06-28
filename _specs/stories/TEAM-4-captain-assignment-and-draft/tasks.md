# TEAM-4 - Captain assignment and draft tasks

- [ ] **M11.TEAM4a** Add `CaptainAssignmentPage`/`CaptainAssignmentPageModel` matching the `captains`
  wireframe, including 2/3/4 captain tabs, checked-in player search, and max-selection enforcement.
- [ ] **M11.TEAM4b** Add `TeamDraftPage`/`TeamDraftPageModel` matching the `draft` wireframe with
  checked-in player search, checkbox rows, assigned-player disabled state, and team count summaries.
- [ ] **M11.TEAM4c** Extend seed game-day state with captain count, two-team/three-team/four-team topology,
  captain assignments, team picks, team lock state, and permission flags.
- [~] **M8.TEAM4d** Backend M8 create-match supports 2/3/4 match topology, captains, and final team assignments; dedicated live draft commands for assign captains, grant scoped draft action,
  pick/unpick players, and lock teams with audit trail.
- [ ] **TEST.TEAM4e** Cover authorization denial, checked-in-first ordering, no duplicate assignment,
  lock behavior, and seed/page-model command state.

**Done when:** admins can assign 2 captains for two teams, 3 captains for three teams, or 4 captains for four teams, only those
captains can pick players, and team assignments are locked before stats/results are published.

