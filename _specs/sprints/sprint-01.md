# Sprint 01 — UI Foundations & Core Session Flow

**Phase:** UI-first (seed data; backend M1–M10 deferred — see [`../design.md`](../design.md) §12)
**Length:** 2 weeks · **Target TFMs:** `net10.0-windows10.0.19041.0`, `net10.0-android`

**Board:** [`sprint-01-board.md`](sprint-01-board.md) — live status (To do / In progress / Done) of every item.

## Sprint goal

> A signed-in player can move through the authenticated Shell and use **Sessions (home) → Session
> detail (RSVP / waitlist) → Profile**, end-to-end, on device, driven entirely by seed data.

This delivers the core "see games → claim a spot → check my stats" loop — the spine of the product —
and proves the SEED-1 swap-by-DI pattern and the M11.0c shared controls before the stats-heavy
screens land in Sprint 02.

## Entry state (already done)

- `M11.0` reusable UI foundation (tokens, 11 controls, Shell theming, UI Library showcase).
- `M11.0a` Font Awesome glyph system.
- `AUTH-7/8/9` Welcome Back sign-in (client slices) — sign-in routes to the Sessions Shell on success.

## Sprint backlog

Estimates in story points (Fibonacci). "Committed" = sprint commitment; "Stretch" pulled in if velocity allows.

| # | Item | Story | Pts | Depends on | Role |
|---|------|-------|----:|-----------|------|
| 1 | **Seed providers + fixtures/state + DI** (`M11.0b`, `M11.0b-tests`) | `SEED-1` | 8 | M11.0 | Dev A |
| 2 | **Shared UI extensions** (`M11.0c`): `LeadingContent` on BrandHeader/PlayerRow; `IconButton`, `IconToggleButton`, `MetadataChip`, `RatingSlider` | `client-ui §6.1` | 5 | M11.0 | Dev B |
| 3 | **Authenticated Shell & bottom tabs** (Sessions/Stats/Profile), routes, post-sign-in transition | `NAV-1` (new, below) | 3 | M11.0, AUTH-8 | Dev B |
| 4 | **Sessions (home) screen** | `SES-6` | 5 | SEED-1, M11.0c, NAV-1 | Dev A |
| 5 | **Session detail + RSVP/waitlist** | `RSVP-8` | 5 | SEED-1, M11.0c, SES-6 | Dev A |
| 6 | **Player profile screen** *(stretch)* | `PROF-5` | 3 | SEED-1, M11.0c, NAV-1 | Dev B |

**Committed:** items 1–5 = **26 pts.** **Stretch:** item 6 (`PROF-5`, +3). If single-dev, move
`PROF-5` to Sprint 02 and treat 1–5 as the commitment.

## Current status (reviewed June 22, 2026)

- **Done (13 pts):** `SEED-1` (8), `M11.0c` (5).
- **In progress (10 pts):** `SES-6` (5), `RSVP-8` (5).
- **In review (3 pts):** `NAV-1` — implementation and automated tests complete; device verification remains.
- **Stretch to do (3 pts):** `PROF-5`.
- Client tests pass in Debug and Release (118/118). Windows Debug/Release and Android Debug builds
  succeed. The builds still report `NU1903` for the legacy SQLite packages, so the repository-wide
  zero-warning quality gate is not yet met.
- Do not start Sprint 02 screens yet. Close the remaining navigation, RSVP wireframe, accessibility,
  responsive-layout, light/dark, and device verification gaps listed on the board.

## Sequencing / critical path
```
Week 1:  SEED-1 (item 1) ─┐         M11.0c (item 2) ─┐
                          │                          │
                          │         NAV-1 (item 3) ──┘  (uses M11.0c shell theming)
Week 2:                   └─► SES-6 (4) ─► RSVP-8 (5)
                          (M11.0c) ───────► PROF-5 (6, stretch)
```

- **Critical path:** `SEED-1` → `SES-6` → `RSVP-8`. SEED-1 must complete its `ISessionsClient` /
  `IRosterClient` surface early (mid-Week-1) to unblock the screens.
- `M11.0c` and `NAV-1` run in parallel with SEED-1 in Week 1 (no data dependency).
- Screens start once SEED-1 + M11.0c + NAV-1 are merged.

## New requirement to create this sprint — `NAV-1`

*As a* signed-in player, *I want* the app to drop me into a tabbed home after sign-in, *so that* I can
move between Sessions, Stats, and Profile.

```gherkin
Scenario: Successful sign-in enters the authenticated shell
  Given a verified session (seed challenge) completes
  Then the Welcome Back route is replaced by an authenticated Shell
  And a bottom tab bar shows Sessions, Stats, and Profile
  And Sessions is the initial tab

Scenario: Tabs switch root sections without losing the shell
  Given the authenticated Shell is shown
  When I select Stats or Profile
  Then the corresponding root page is shown with the tab marked active
  And the sign-in route is not reachable by back navigation

Scenario: Shell uses the brand system
  Then the tab bar, header, and surfaces use BrandStyles/BrandColors (no page-local hex)
  And tab icons are Font Awesome glyphs with semantic names (INV-13)
```

Promote `NAV-1` to `_specs/stories/NAV-1-authenticated-shell/` (requirements/design/tasks) at sprint start.

## Definition of Done (every committed item)

- Builds clean on `net10.0-windows10.0.19041.0` and `net10.0-android` (no new warnings).
- `Client.Tests` green against the **seed** providers (page-model state, commands, navigation, states).
- Screen composition matches `documentation/mobile-wireframes.html`; **no emoji, no page-local hex/font sizes** (INV-13, token-driven).
- `StateView` loading/empty/error/offline wired; accessibility pass (semantic names, ≥44px, large-text, narrow-width); light + dark verified.
- Page models depend only on SEED-1 client interfaces; no API/`HttpClient` references.

## Sprint review / demo

Tap-through on a Windows + Android device, seed mode: sign in → land on **Sessions** → open a session →
toggle **RSVP / join waitlist** (state persists via `SeedState`) → open **Profile** and see career
tiles. Reset seed state to show determinism.

## Out of scope (Sprint 02 preview)

`LEAD-4` Leaderboard, `STAT-7` Match stats (submit + confirm), `STAT-8` Rate teammates, plus polish.
**Pre-req for Sprint 02:** resolve the match-stats **confirmation-model decision** (simple
self-submit→single-confirm per wireframe vs. the dual-captain
[`match-stats-confirmation-architecture-plan.md`](../../documentation/match-stats-confirmation-architecture-plan.md))
before `STAT-7` starts.

## Risks & mitigations

| Risk | Mitigation |
|------|------------|
| SEED-1 surface incomplete → screens blocked | SEED-1 owns the full first-wave surface; finish `ISessionsClient`/`IRosterClient`/`IProfileClient` by mid-Week-1; screen devs review the interface stubs Day 2. |
| `M11.0c` control bugs ripple into screens | Land M11.0c behind UI Library showcase examples + control-level tests before screens consume them. |
| Shell/back-stack lets sign-in be reached after auth | `NAV-1` scenario asserts sign-in is unreachable by back nav. |
| Dark-mode contrast regressions | DoD includes a light/dark pass per screen. |
| Scope creep from audit nuances | Deferred by decision; not in this sprint. |

## Decisions needed (PM)

1. Team size / who owns Dev A vs Dev B (affects whether `PROF-5` is committed or stretch).
2. ~~Confirm `NAV-1` is created as a story at sprint start.~~ Resolved: story folder and tasks exist.
3. Lock the Sprint-02 stats-confirmation model (so STAT-7 isn't blocked next sprint).
