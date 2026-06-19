# LEAD-4 — Leaderboard screen

**Epic:** LEAD — Leaderboards & Career stats · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `INV-6`/`INV-7` (derived-on-read stats),
`LEAD-1` (season read projections), `LEAD-2` (career figures), `LEAD-3` (tie-breakers),
`NFR-Accessibility`, `NFR-Iconography` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the `leaderboard` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
**Phase:** UI-first — data comes from a seed `ILeaderboardClient`; the backend is deferred (see [`../../design.md`](../../design.md) §12).

## Story

*As a* SouthBaySoccer player, *I want* a season leaderboard I can switch between goals, assists,
rating, and MVP, *so that* I can see who is leading each axis and open any player's profile.

This screen directly implements the `leaderboard` wireframe and is the **Stats** destination of the
bottom tab bar. Each ranking is a read projection (`LEAD-1`/`LEAD-2`); in this phase the projections
are served by a seed `ILeaderboardClient`.

## Acceptance criteria

```gherkin
Scenario: The screen matches the leaderboard wireframe hierarchy
  Given I am on the Stats tab
  When the Leaderboard screen is displayed
  Then the title displays "Leaderboard"
  And a season Badge displays "Season 2026"
  And a SegmentedControl shows the metrics "Goals", "Assists", "Rating", and "MVP"
  And a ranked list shows one row per player in descending order of the selected metric
  And each row shows a rank, an Avatar, a player name, a sub-detail of "position · apps", and the metric value
  And a footnote describes the inclusion and tie-break rules for the selected metric
  And the bottom tab bar is shown with "Stats" active

Scenario: Each metric tab loads its own ranked list from the seed leaderboard client
  Given the Leaderboard screen is displayed
  When I select the "Goals" metric
  Then the page model requests the goals ranking for the current season from the ILeaderboardClient
  And the list re-renders ranked by goals
  When I select the "Assists" metric
  Then the page model requests the assists ranking
  And the list re-renders ranked by assists
  And the same applies to the "Rating" and "MVP" metrics
  And switching the metric re-queries and re-renders without leaving the screen

Scenario: The first-placed player is visually distinguished
  Given a ranked list is displayed for the selected metric
  Then the rank 1 row is highlighted as the leader with the gold treatment
  And its rank shows the trophy glyph instead of the number
  And the remaining rows show their numeric rank in descending order

Scenario: The inclusion and tie-break footnote is present
  Given the "Goals" metric is selected
  Then the footnote states that only captain or admin-confirmed goals count
  And the footnote states that ties break by fewer appearances, then assists
  And selecting another metric replaces the footnote with that metric's inclusion and tie-break rule

Scenario: Tapping a row opens that player's profile
  Given a ranked list is displayed
  When I tap a player's row
  Then the app navigates to that player's Profile

Scenario: Loading, empty, error, and offline states use StateView
  Given the Leaderboard screen requests a ranking
  When the request is in flight
  Then a loading state is shown
  When the selected metric has no ranked players
  Then an empty state is shown
  When the request fails or the device is offline
  Then a recoverable error or offline state is shown with a retry action
  And retry re-requests the current metric's ranking

Scenario: Iconography uses Font Awesome instead of emoji
  Given the Leaderboard screen shows the leader trophy, season badge, and tab pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Leaderboard screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And the ranked list remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`LEAD-1`](../../requirements.md#epic-lead--leaderboards--career-stats) — season read projections this screen presents.
- [`LEAD-2`](../../requirements.md#epic-lead--leaderboards--career-stats) — career figures shown when a row opens a Profile.
- [`LEAD-3`](../../requirements.md#epic-lead--leaderboards--career-stats) — tie-break rules surfaced in the footnote.
