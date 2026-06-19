# LEAD-4 — Leaderboard screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the LEAD-4 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [ ] **M11.LEAD4.a** Implement `LeaderboardPage` and `LeaderboardPageModel` directly from the
  `leaderboard` wireframe: title + season `Badge`, `SegmentedControl` (Goals/Assists/Rating/MVP),
  ranked `PlayerRow` list using `LeadingContent` for rank/trophy plus avatar/name/sub-detail/value,
  gold rank-1 leader treatment
  with the Font Awesome `trophy` glyph, inclusion/tie-break footnote, and the Stats-active Shell tab.
  Use only shared brand resources and Font Awesome glyphs with semantic descriptions; no emoji or
  page-local hex. Page code-behind is `InitializeComponent` only. Bind the complete ranking contract
  supplied by SEED-1; do not redefine or extend its fixtures.
  — Stories: `LEAD-4`, `INV-13` · Projects: MAUI client · Depends on: M11.0c, M11.0b.

- [ ] **M11.LEAD4.b** Wire metric switching and navigation in the page model: `SelectedMetric`,
  `Season`, `Rankings`, `IsBusy`; `SelectMetricCommand` re-queries the `ILeaderboardClient` and swaps
  the list/footnote without leaving the screen; `OpenPlayerCommand` navigates to the tapped player's
  Profile; `RefreshCommand` re-requests the current metric. Guard against concurrent queries.
  — Stories: `LEAD-4`, `LEAD-2` · Projects: MAUI client · Depends on: M11.LEAD4.a.

- [ ] **M11.LEAD4.c** Bind the `StateView` loading/empty/error/offline states around the ranked list
  with a retry that re-requests the current metric.
  — Stories: `LEAD-4` · Projects: MAUI client · Depends on: M11.LEAD4.b.

- [ ] **M11.LEAD4.d** `Client.Tests`: the page model exposes the four metrics, season label, and
  wireframe header/footnote copy; selecting each metric calls `ILeaderboardClient` for that axis and
  swaps `Rankings` (segment switch swaps the ranking); seed order and `LEAD-3` tie-breaks are
  preserved (no re-sort); rank 1 is flagged as leader and ranks are sequential; `OpenPlayerCommand`
  navigates once; loading/empty/error/offline map to the right `StateView` state and retry
  re-requests; icon semantics and large-text/narrow-width scroll hold. Build
  `net10.0-windows10.0.19041.0`.
  — Stories: `LEAD-4`, `INV-13` · Depends on: M11.LEAD4.c.

**Prerequisites:** M11.0c (shared first-wave UI extensions), M11.0b (seed-client seam / DI
registration).

**Done when:** the screen reproduces the `leaderboard` wireframe exactly from shared resources, all
LEAD-4 scenarios have passing `Client.Tests`, metric switching re-queries the seed `ILeaderboardClient`
and re-renders, no emoji/raw hex are used, and the client builds.
