# Sprint 01 — Storyboard / Board

Living tracker for [`sprint-01.md`](sprint-01.md). Columns mirror the per-story `tasks.md` checkboxes
(`[ ]` to do · `[~]` in progress · `[x]` done). When a story's tasks change, move its card between
columns and update the snapshot.

**Status keys:** `To do` · `In progress` · `In review` · `Done` · `Blocked`.

## Snapshot (commitment = items 1–5; PROF-5 = stretch)

| Metric | Pts |
|--------|----:|
| Committed | 26 |
| Done | 13 |
| In progress | 0 |
| In review | 13 |
| To do | 26 |
| Stretch (PROF-5) | 3 |
| Planned Sprint 03 | 26 |

_Entry-state foundations (`M11.0`, `M11.0a`) and the AUTH sign-in client slices are already done and
are listed under Done for context; they are not counted in the 26-pt commitment. Sprint 03 items are
newly planned and also not counted in the Sprint 01 commitment._

## Stories — requirements checklist

Every story requirement, one line each. `[x]` = Definition of Done met · `[ ]` = not yet. Sprint tag in (parens). Mirrors each story's `tasks.md`.

**Foundations**
- [x] **M11.0** — Reusable UI foundation: brand tokens, 11 controls, Shell theming, UI Library showcase.
- [x] **M11.0a** — Font Awesome glyph system: Solid + Brands fonts + typed glyph catalog.
- [x] **M11.0c** — Shared UI extensions: `LeadingContent` on BrandHeader/PlayerRow + `IconButton`, `IconToggleButton`, `MetadataChip`, `RatingSlider`. (S1)

**Authentication (sign-in)**
- [ ] **AUTH-7** — Welcome Back WhatsApp sign-in screen, the first app route. _(client screen done; automated large-text/narrow visual check remains)_
- [ ] **AUTH-8** — Request + verify the one-time WhatsApp sign-in challenge. _(client slice done; verify/exchange + Functions tests deferred to the backend phase)_
- [x] **AUTH-9** — Open the Pickup Pal bot / signup external actions.

**Sprint 01 — UI foundations & core session flow**
- [x] **SEED-1** — Seed-data providers (interfaces + immutable fixtures + resettable state) behind the client services. (S1)
- [ ] **NAV-1** — Authenticated Shell + bottom tabs (Sessions/Stats/Profile); sign-in → Shell. (S1)
- [ ] **SES-6** — Sessions home: upcoming list, dues status, submit-stats prompt. (S1) _(client + automated semantic/responsive checks done; light/dark device sign-off pending — In review)_
- [ ] **RSVP-8** — Session detail: going + waitlist lists, RSVP/waitlist action. (S1) _(client + collapsed going roster + automated semantic/responsive checks done; light/dark device sign-off pending — In review)_
- [ ] **PROF-5** — Player profile: career stat tiles, recent form. (S1 stretch) _(client + automated semantic/responsive checks done; light/dark device sign-off pending — In review)_

**Sprint 02 — stats wave**
- [x] **LEAD-4** — Leaderboard: Goals/Assists/Rating/MVP segments. (S2)
- [x] **STAT-7** — Match stats: self-submit goals/assists + captain confirm. (S2)
- [x] **STAT-8** — Rate teammates: 0–10 rating, like, single MVP. (S2)

**Sprint 03 - admin setup and game-day operations**
- [ ] **ADMIN-4** - Create session: admin enters game date, time, location, setup, then publishes to team RSVP feed. (S3)
- [ ] **GDAY-1** - Game Day tab: player self-check-in between 7:30 PM and 7:45 PM, closed/override states. (S3)
- [ ] **TEAM-4** - Captain assignment + team draft: admin selects 2, 3, or 4 captains; captains get session-scoped pick permissions. (S3)
- [ ] **STAT-9** - Post-game captain approval/results: approve goals/assists, record W/D/L, derive recent form for teammates. (S3)

## Board

### To do

| Card | Story | Pts | Tasks | Notes |
|------|-------|----:|-------|-------|
| **Create and publish session** | `ADMIN-4` | 5 | page+model `[ ]` / seed publish `[ ]` / backend validation `[ ]` / tests `[ ]` | Admin creates a game date/time/location/setup and publishes it into the player RSVP feed. |
| **Game Day check-in tab** | `GDAY-1` | 5 | page+model `[ ]` / seed state `[ ]` / backend window `[ ]` / tests `[ ]` | Player check-in from 7:30 PM-7:45 PM; RSVP remains intent; late override is GameAdmin-only and audited. |
| **Captain assignment + team draft** | `TEAM-4` | 8 | admin assign `[ ]` / scoped permission `[ ]` / draft UI `[ ]` / lock `[ ]` / tests `[ ]` | Admin chooses 2, 3, or 4 captains from checked-in players only; selection max follows the chosen tab; assigned captains pick teams from searchable checked-in list. |
| **Post-game captain approval + results** | `STAT-9` | 8 | approval queue `[ ]` / result entry `[ ]` / projection `[ ]` / conflict review `[ ]` / tests `[ ]` | Captains approve goals/assists after play; team W/D/L and validated rotation counters persist through MatchResult and derived profile recent form. |

### In progress

_(none)_

### In review

| Card | Story | Pts | Tasks | Remaining |
|------|-------|----:|-------|-----------|
| **Authenticated Shell & tabs** | `NAV-1` | 3 | `M11.NAV1.a` `[x]` · `M11.NAV1.b` `[x]` · `M11.NAV1.c` `[~]` | Manual Windows/Android verification: tab switching, root-tab back behavior, screen reader, and light/dark modes. |
| **Sessions (home) screen** | `SES-6` | 5 | page+model `[x]` · seed bind `[x]` · states `[x]` · tests `[x]` | Code, tests (141/141), and automated semantic/responsive + theme-token checks done; both TFMs build clean. Remaining: interactive on-device light/dark spot-check (Windows + Android). |
| **Session detail + RSVP/waitlist** | `RSVP-8` | 5 | page+model `[x]` · seed bind `[x]` · RSVP/waitlist `[x]` · states `[x]` · tests `[x]` | Collapsed going roster (`+ N more going`), tests, and automated semantic/responsive + theme-token checks done; both TFMs build clean. Remaining: interactive on-device light/dark spot-check (Windows + Android). |
| **Player profile** *(stretch)* | `PROF-5` | 3 | page+model `[x]` · seed bind `[x]` · states `[x]` · tests `[x]` | Profile implementation, zero-state, navigation, semantic/responsive tests, and Windows/Android builds are complete. Remaining: interactive on-device light/dark spot-check. |

### Done

| Card | Story | Notes |
|------|-------|-------|
| Reusable UI foundation | `M11.0` | tokens, 11 controls, Shell theming, UI Library showcase |
| Font Awesome glyph system | `M11.0a` | Solid + Brands fonts, typed glyph catalog |
| Shared UI extensions | `M11.0c` | LeadingContent slots, icon/toggle/chip/slider styles, showcase, and XAML contract tests |
| Seed providers + fixtures/state + DI | `SEED-1` | complete seed client set, immutable fixtures, resettable state, DI selection, Release guard, and tests |
| Welcome Back screen | `AUTH-7` (`M11.3a`) | client done; `M11.3d` large-text/narrow visual check carryover |
| Continue with WhatsApp (client) | `AUTH-8` (`M11.3b`) | client challenge/deep-link done |
| Pickup Pal actions | `AUTH-9` (`M11.3b/d`) | done |
| Leaderboard screen | LEAD-4 | Goals/Assists/Rating/MVP segments, seed-backed metric switching, tests, and Windows/Android builds clean. |
| Match stats screen | STAT-7 | Self-submit goals/assists, pending lock, captain confirm, Rate teammates route, tests, and Windows/Android builds clean. |
| Rate teammates screen | STAT-8 | 0-10 ratings, likes, single MVP, rater exclusion, submit flow, tests, and Windows/Android builds clean. |

### Blocked / backend-deferred (carryover, not Sprint-01 scope)

| Card | Story | Why |
|------|-------|-----|
| Typed API pipeline | `M11.1` (`[~]`) | swaps seeds for the real API later; not needed in the UI phase |
| Challenge verify/exchange + Functions tests | `AUTH-8` (`M11.3c/3d` `[~]`) | server endpoints blocked on backend `M3`; client uses the seed challenge now |
| 4-captain topology decision | `TEAM-4` | resolved: 3 captains means 3 teams and 4 captains means 4 teams; backend/team-draft design should model two-, three-, and four-team rotation formats |

## Critical path

`SEED-1` (interfaces by mid-Week-1) → `SES-6` → `RSVP-8`. `M11.0c` and `NAV-1` run in parallel in
Week 1. Screens start once `SEED-1` + `M11.0c` + `NAV-1` are merged.

## Review evidence and next action

- Verified 2026-06-22 (`claude/sessions-flow`): `SouthBaySoccer.Client.Tests` passes in Debug and
  Release (141/141) after the `SES-6` + `RSVP-8` closure pass.
- `RSVP-8`: added the wireframe's collapsed going roster — a four-row preview above a "+ N more going"
  affordance (`GoingPreview` / `MoreGoingCount` / `HasMoreGoing` / `MoreGoingLabel`), with `GoingHeading`
  keeping the full count — plus page-model tests for both the collapsed and within-limit cases.
- `SES-6` + `RSVP-8`: added `SessionScreensXamlTests` automated semantic/responsive contract over the
  shipped page XAML (informational/interactive icon semantic descriptions, `ScrollView` so content is
  uncut at large text, typed Font Awesome glyphs + no Unicode emoji per `INV-13`, and theme-token
  colours / no raw hex). `/code-review` pass run (dotnet + maui-xaml reviewers); one accessibility nit
  applied (removed a redundant `SemanticProperties.Description` on the non-interactive "+ N more going"
  row).
- Windows (`net10.0-windows10.0.19041.0`) and Android (`net10.0-android`) builds succeed with **zero
  new warnings** — only the pre-existing `CS0618` `DisplayAlert` obsoletion in `App.xaml.cs`
  remains (untouched).
- Integrated `codex/profile`: PROF-5 profile implementation and tests are present; the SQLite
  dependency graph now uses `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3, and `dotnet list package
  --vulnerable --include-transitive` reports no vulnerable packages.
- Remaining for `SES-6` / `RSVP-8` Done: the interactive on-device light/dark visual spot-check on
  Windows + Android. Theming is structurally verified (all theme-sensitive colours resolve via
  `AppThemeBinding` light/dark tokens, asserted by the no-raw-hex test), so this is a visual sign-off.
- Verified 2026-06-25 (`codex/stats-wave`): `SouthBaySoccer.Client.Tests` passes (197/197), and both `net10.0-windows10.0.19041.0` and `net10.0-android` MAUI builds succeed with zero warnings after LEAD-4, STAT-7, and STAT-8 integration.
- Recommended next action: visual light/dark sign-off on `NAV-1` / `SES-6` / `RSVP-8` / `PROF-5`.

## How to keep this current

1. As a task flips `[ ]`→`[~]`→`[x]` in a story's `tasks.md`, move that card to **In progress** /
   **Done** here and tick its task box (`[ ]` to `[x]`).
2. Recompute the snapshot points (Done / In progress / To do).
3. A card is **Done** only when its story's Definition of Done in `tasks.md` is met
   (builds, seed-backed `Client.Tests` green, wireframe match, no emoji/raw hex, accessibility + light/dark).
4. Note new blockers under **Blocked** with the reason.

