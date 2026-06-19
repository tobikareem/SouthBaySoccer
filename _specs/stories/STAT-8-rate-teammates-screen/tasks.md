# STAT-8 — Rate teammates screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the STAT-8 slice of milestone **M11**; the full milestone roadmap, the UI-first delivery strategy,
and the dependency graph live in [`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in
progress · `[x]` done.

- [ ] **M11.STAT8.a** Implement `RateTeammatesPage` and `RateTeammatesPageModel` directly from the
  `rate` wireframe: a `BrandHeader` with back + `Rate the match` / `Sat · Marina Field`, the intro
  copy `Rate teammates 0–10, like a great game, and pick one MVP. You can't rate yourself.`, a
  `CollectionView` of teammate `BrandCard`s (Avatar, name, sub-detail, shared `IconToggleButton`
  actions, shared `RatingSlider` + value readout), and a `Submit ratings` `PrimaryButton`. Use only shared
  brand resources and Font Awesome glyphs (`arrow-left`, `heart`, `star`) with semantic descriptions;
  no emoji or page-local hex. Page code-behind only calls `InitializeComponent`.
  — Stories: `STAT-8`, `INV-13` · Projects: MAUI client · Depends on: M11.0c, M11.0b.

- [ ] **M11.STAT8.b** Bind `Teammates`, `MatchSubtitle`, `SelectedMvp`, `IsBusy`, and `State` in
  `RateTeammatesPageModel` to the seed `IStatsClient`: load the current match's rateable teammates
  **excluding the rater** (`INV-8`, no self-vote) and submit per-teammate rating + like + the single
  MVP through the complete `IStatsClient` seam and fixtures supplied by SEED-1. This story must not
  add Seed methods or fixtures; it consumes the deterministic teammates and excludes the rater.
  — Stories: `STAT-8`, `SEED-1` · Projects: MAUI client · Depends on: M11.0b.

- [ ] **M11.STAT8.c** Enforce the rating/like/MVP rules in the page model: coerce each row's `Rating`
  to an integer in `[0,10]` (`INV-8`); `ToggleLikeCommand` flips only the target row's `Liked`
  (one like per peer per match, `STAT-4`); `SelectMvpCommand` keeps MVP single-select across the list
  and clears it when the marked teammate is re-selected (`STAT-5`); `SubmitRatingsCommand` runs once
  while `IsBusy` and excludes the rater; `BackCommand` returns to the previous screen. Wrap the list +
  submit in `StateView` for loading / empty / error / offline / content with a retry action.
  — Stories: `STAT-8`, `INV-8`, `STAT-3`, `STAT-4`, `STAT-5` · Projects: MAUI client · Depends on: M11.STAT8.a, M11.STAT8.b.

- [ ] **M11.STAT8.d** (STAT-8 slice) `Client.Tests`: appearance loads rateable teammates through a
  mocked `IStatsClient` and exposes one wireframe-shaped row per teammate; the rater never appears in
  `Teammates` (no self-vote); each `Rating` is an integer constrained to `[0,10]`; `ToggleLikeCommand`
  affects only its row; `SelectMvpCommand` keeps exactly one MVP and clears on re-select;
  `SubmitRatingsCommand` sends every rating + like + the single MVP for the current match, excludes
  the rater, and runs once while busy; empty/error/offline drive `StateView` and retry re-requests;
  `BackCommand` navigates; icon controls expose semantic descriptions and the list stays scrollable
  and uncut at large text and the narrowest width. Build `net10.0-windows10.0.19041.0`.
  — Stories: `STAT-8`, `INV-13` · Projects: MAUI client · Depends on: M11.STAT8.c.

**Prerequisites:** M11.0c (shared first-wave UI extensions) and M11.0b (seed-data providers /
`IStatsClient`). **Related task slices:**
[`LEAD-4`](../LEAD-4-leaderboard-screen/) (Rating/MVP leaderboard axes this screen feeds),
[`SEED-1`](../SEED-1-seed-data-providers/tasks.md) (the seed `IStatsClient` and fixtures).

**Done when:** the screen reproduces the `rate` wireframe exactly from shared resources; teammates
load from the seed `IStatsClient` with the rater excluded; each teammate has an independent 0–10
integer rating and like, and MVP is single-select across the list; submit sends ratings/likes/MVP;
loading/empty/error/offline render through `StateView`; no emoji or raw hex are used; all STAT-8
`Client.Tests` pass; and the client builds.
