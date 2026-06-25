# STAT-7 — Match stats screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the STAT-7 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [x] **M11.S7a** Implement `MatchStatsPage` and `MatchStatsPageModel` directly from the `matchstats`
  wireframe: branded header with back button, confirmation notice, "Your performance" section, the
  Goals/Assists `CounterStepper` card, the "Submit for confirmation" primary button, the pending
  note, the "Confirm teammates · captain" list, and the "Rate teammates instead" link. Use only
  shared brand resources and Font Awesome glyphs (semantic names, no emoji or page-local hex);
  page code-behind is `InitializeComponent` only.
  — Stories: `STAT-7`, `INV-13` · Projects: MAUI client · Depends on: M11.0, M11.0a, M11.0b.

- [x] **M11.S7b** Bind the screen to the seed `IStatsClient` (`SouthBaySoccer/SeedData/SeedStatsClient.cs`):
  load the player's current Goals/Assists and the teammate submissions (one pre-confirmed row plus
  unconfirmed rows) as deterministic wireframe fixtures. Page model depends on the `IStatsClient`
  abstraction only; registered by DI per `../../design.md` §12.
  — Stories: `STAT-7`, `STAT-2` · Projects: MAUI client · Depends on: M11.0b, M11.S7a.

- [x] **M11.S7c** Implement the submit  pending behavior: `IncrementGoals/Assists` and
  `DecrementGoals/Assists` clamp at zero; `SubmitCommand` calls the seed client, flips `SubmitState`
  to pending, disables resubmission, and locks the steppers; the pending note appears. Coalesce
  repeated taps while busy/pending into one submission.
  — Stories: `STAT-7`, `STAT-1` · Projects: MAUI client · Depends on: M11.S7b.

- [x] **M11.S7d** Implement the captain confirm action: `ConfirmTeammateCommand` confirms a
  teammate's submission through the seed client and marks the row confirmed optimistically (the
  "Confirm" ghost action is replaced by the confirmed check glyph). Wire `OpenRateCommand` to the
  Rate teammates screen and `BackCommand` to back navigation.
  — Stories: `STAT-7`, `STAT-6` · Projects: MAUI client · Depends on: M11.S7b.

- [x] **M11.S7e** Wrap the content in `StateView` and wire loading/empty/error/offline + `RetryCommand`
  to the seed load (entered totals preserved on error).
  — Stories: `STAT-7` · Projects: MAUI client · Depends on: M11.S7b.

- [x] **M11.S7f** `Client.Tests` (STAT-7 slice): page-model wireframe copy; stepper increment/decrement
  with the zero floor; submit  pending disables resubmission and calls the seed client once; confirm
  marks a teammate confirmed optimistically; seed-sourced data and loading/empty/error/offline map to
  `StateView`; `OpenRateCommand`/`BackCommand` navigate; icon semantic descriptions; scrollable and
  uncut at large text and the narrowest width. Build `net10.0-windows10.0.19041.0`.
  — Stories: `STAT-7`, `INV-13` · Depends on: M11.S7c, M11.S7d, M11.S7e.

**Prerequisites:** M11.0 (reusable UI foundation), M11.0a (Font Awesome resources + glyph catalog),
M11.0b (seed-data providers / `IStatsClient`).

**Done when:** the screen reproduces the `matchstats` wireframe exactly from shared resources, all
STAT-7 scenarios have passing `Client.Tests` against the seed `IStatsClient`, no emoji/raw hex are
used, and the client builds. The confirmation-model open decision (see [`design.md`](design.md)) is
honored: submit/confirm stay behind `IStatsClient` so the heavier model can be adopted without page
rework.
