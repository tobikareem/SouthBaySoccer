# STAT-7 — Match stats screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, persistence, seed-data strategy) lives in [`../../design.md`](../../design.md)
(see §12, UI-first seed data); the reusable UI contract is [`../../client-ui.md`](../../client-ui.md);
the visual source of truth is
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html)
(the `matchstats` screen). This screen's Font Awesome contract implements `INV-13`.

This is a UI-first screen: it composes the shared control library against deterministic fixtures
from a seed `IStatsClient`. No Function App, database, or typed API client is involved yet; the seed
client is swapped for the real `IStatsClient` at M11.1 with no page or page-model change (`../../design.md` §12).

## Screen composition

`MatchStatsPage` uses a vertically scrollable layout matching the wireframe order/hierarchy. Every
color, radius, size, and text style comes from `BrandColors.xaml` / `BrandTokens.xaml` /
`BrandStyles.xaml`; the page adds no raw hex, font sizes, magic-number spacing, or emoji.

1. **`BrandHeader`** (Green-to-Pine) with `ShowBack=true` and `BackCommand`. `Title` = `Match stats`;
   `Subtitle` = the match context (wireframe sample `Sat · Marina Field`).
2. **Info `NoticeSurface`** (info glyph + copy): explains that a captain or game admin confirms every
   submission before it reaches the leaderboard. Sets the player's expectation per `STAT-6`.
3. **`SectionHeader`** `Your performance`.
4. **`BrandCard`** containing two **`CounterStepper`** rows:
   - Goals — `Glyph` = futbol, two-way `Value` bound to `Goals`, `Minimum=0`.
   - Assists — `Glyph` = shoe/boot, two-way `Value` bound to `Assists`, `Minimum=0`.
   Both steppers disable their decrement at zero and stop accepting edits once the submission is
   pending (`SubmitState != Editable`).
5. **`PrimaryButton`** `Submit for confirmation` bound to `SubmitCommand`. It carries a **pending
   state**: once submitted it shows the pending label and is disabled against resubmission
   (`CanSubmit == false`). The button label/availability is driven entirely by `SubmitState` and
   `IsBusy` — no code-behind toggling.
6. **Submit note** (`TextCaption`) with a connection glyph: `Sent to Pickup Pal · pending captain/admin`,
   visible in the pending state.
7. **`SectionHeader`** `Confirm teammates · captain`.
8. **Teammate confirmation list** of **`PlayerRow`** items bound to `TeammateSubmissions`. Each row
   shows the teammate's avatar/initials, name, and submitted totals as `Detail` (e.g.
   `submitted: 1 goal`). Unconfirmed rows expose a `Confirm` ghost action (`TrailingContent`) bound
   to `ConfirmTeammateCommand` with the row as parameter; confirmed rows show a confirmed check glyph
   instead (one row is pre-confirmed in the seed, per the wireframe).
9. **`Rate teammates instead`** link (chevron glyph) bound to `OpenRateCommand` → the Rate screen.

The whole content area is wrapped by a **`StateView`** so loading / empty / error / offline render
through the shared control rather than page-local layouts (`../../client-ui.md` §6 control catalog).

### Tokens & controls used

- Controls: `BrandHeader`, `SectionHeader`, `BrandCard`, `CounterStepper`, `PlayerRow`, `StateView`,
  and the `PrimaryButton` / `GhostButton` / `LinkButton` styles — all from `../../client-ui.md`.
- Surfaces/typography: `NoticeSurface`, `CardSurface`, `TextLabel`, `TextH2`, `TextBody`,
  `TextCaption`; spacing `SpaceSm`/`SpaceMd`/`SpaceLg`; touch targets ≥ `TouchMin` (44).
- No bespoke one-off styles are introduced for these wireframe patterns.

## Font Awesome contract (`INV-13`)

Pictograms use the bundled Font Awesome Free fonts already registered for the client
(`FontAwesomeSolid` / `FontAwesomeBrands`) and referenced through the typed glyph catalog
(`Resources/Fonts/FontAwesomeGlyphs.cs`) — no inline Unicode literals, no emoji. Each icon also
carries a `SemanticProperties.Description`. Required glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Goals row | Solid | `futbol` |
| Assists row | Solid | `shoe-prints` (boot) |
| Confirmation notice | Solid | `circle-info` |
| Submit / pending note | Solid | `plug` / `plug-circle-check` |
| Teammate confirmed | Solid | `circle-check` |
| Rate teammates link | Solid | `chevron-right` |

Font Awesome is for pictograms only; body copy stays on the registered Open Sans / Segoe Semibold
brand typography.

## MVVM, navigation & state

`MatchStatsPage` → `MatchStatsPageModel`; the page code-behind contains only `InitializeComponent`.
All adjustment, submission, confirmation, and navigation logic lives in the page model — no business
logic in XAML code-behind, and the page binds to `BindableProperty` inputs / `ICommand` outputs only.

`MatchStatsPageModel` owns:

- `Goals` (int), `Assists` (int) — two-way to the steppers; clamped at `Minimum=0`.
- `SubmitState` (enum `Editable | Submitting | Pending`) — drives the primary button label,
  `CanSubmit`, and whether the steppers accept edits.
- `TeammateSubmissions` — observable collection of teammate submission view items (initials, name,
  submitted totals, `IsConfirmed`).
- `IsBusy` (bool) and a `StateView`-bound state (`Loading | Empty | Error | Offline | Content`).
- Commands:
  - `IncrementGoalsCommand` / `DecrementGoalsCommand`, `IncrementAssistsCommand` /
    `DecrementAssistsCommand` (decrement no-ops at zero; the stepper control itself disables at the
    boundary).
  - `SubmitCommand` — guarded by `CanSubmit`; calls the seed client, sets `SubmitState=Submitting`
    then `Pending`; disabled against resubmission while pending or busy.
  - `ConfirmTeammateCommand` — takes the teammate row, calls the seed client to confirm, then marks
    the row `IsConfirmed` optimistically.
  - `OpenRateCommand` — Shell navigation to the Rate teammates screen for the same match.
  - `BackCommand` — navigates back (bound to the header).
  - `RetryCommand` — re-runs the initial load for the `StateView` error/offline state.

### Seed dependency

The page model depends only on the client abstraction **`IStatsClient`** (`../../design.md` §12).
The seed implementation (`SouthBaySoccer/SeedData/SeedStatsClient.cs`) returns deterministic
fixtures matching the wireframe: the current player's `Goals`/`Assists` (sample `2` / `1`), and a
teammate list with one pre-confirmed row (`Jide D. · 1 goal · 2 assists`) plus unconfirmed rows
(`Sade M. · submitted: 1 goal`, `Tunde B. · submitted: 2 goals`). Submit and confirm mutate the seed
state and are reflected optimistically. Seeds carry no real personal data and are excluded from
Release / swapped at M11.1.

### States

- **Loading** — initial fetch of player totals + teammate submissions.
- **Content** — steppers editable, submit available.
- **Pending** — after submit; submit disabled, pending note shown, steppers locked.
- **Empty** — no teammate submissions to confirm (empty `StateView` for the list region).
- **Error** — recoverable seed/client failure; retry available, entered totals preserved.
- **Offline** — no connectivity; retry available.

## Open decision — confirmation model not yet finalized

This screen intentionally implements the **current wireframe's simple model**: a player self-submits
goals/assists, and a single captain or game admin confirms each submission. A heavier alternative is
proposed in `documentation/match-stats-confirmation-architecture-plan.md` (per-goal claims with
attributed assists, and dual-captain review). **The confirmation model is an open decision and may
change.** Keep `IStatsClient`'s submit/confirm surface narrow and behind the interface so the UI can
adopt the heavier model later without reworking the page. This open decision is also recorded in the
solution decision log (`../../design.md` §10).

## Test design (`Client.Tests`) — STAT-7 slice

- the page model exposes the wireframe copy (`Match stats`, `Your performance`, the confirmation
  notice, `Sent to Pickup Pal · pending captain/admin`, `Confirm teammates · captain`);
- increment/decrement adjust `Goals`/`Assists`; decrement does not go below zero;
- `SubmitCommand` transitions `SubmitState` to `Pending`, disables resubmission, and calls the seed
  client once; repeated taps while busy/pending produce one submission;
- `ConfirmTeammateCommand` marks the targeted teammate `IsConfirmed` and is reflected optimistically;
- data is sourced from the seed `IStatsClient`; loading/empty/error/offline map to the `StateView`
  state; `RetryCommand` re-loads;
- `OpenRateCommand` navigates to the Rate teammates screen; `BackCommand` navigates back;
- icon controls expose semantic descriptions; the page stays scrollable and uncut at large text and
  the narrowest supported width. Build `net10.0-windows10.0.19041.0`.
