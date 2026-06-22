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
| In progress | 10 |
| In review | 3 |
| To do | 0 |
| Stretch (PROF-5) | 3 |

_Entry-state foundations (`M11.0`, `M11.0a`) and the AUTH sign-in client slices are already done and
are listed under Done for context; they are not counted in the 26-pt commitment._

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
- [ ] **SES-6** — Sessions home: upcoming list, dues status, submit-stats prompt. (S1)
- [ ] **RSVP-8** — Session detail: going + waitlist lists, RSVP/waitlist action. (S1)
- [ ] **PROF-5** — Player profile: career stat tiles, recent form. (S1 stretch)

**Sprint 02 — stats wave**
- [ ] **LEAD-4** — Leaderboard: Goals/Assists/Rating/MVP segments. (S2)
- [ ] **STAT-7** — Match stats: self-submit goals/assists + captain confirm. (S2)
- [ ] **STAT-8** — Rate teammates: 0–10 rating, like, single MVP. (S2)

## Board

### To do

| Card | Story | Pts | Tasks | Depends on | Owner |
|------|-------|----:|-------|-----------|-------|
| **Player profile** *(stretch)* | `PROF-5` | 3 | page+model `[ ]` · seed bind `[ ]` · states `[ ]` · tests `[ ]` | SEED-1, M11.0c, NAV-1 | — |

### In progress

| Card | Story | Pts | Tasks | Remaining |
|------|-------|----:|-------|-----------|
| **Sessions (home) screen** | `SES-6` | 5 | page+model `[x]` · seed bind `[x]` · states `[x]` · tests `[~]` | Add semantic/responsive automated checks and complete light/dark Windows + Android verification. |
| **Session detail + RSVP/waitlist** | `RSVP-8` | 5 | page+model `[~]` · seed bind `[~]` · RSVP/waitlist `[x]` · states `[x]` · tests `[~]` | Add the wireframe's collapsed going roster / `+ 12 more going`, then semantic/responsive and light/dark device verification. |

### In review

| Card | Story | Pts | Tasks | Remaining |
|------|-------|----:|-------|-----------|
| **Authenticated Shell & tabs** | `NAV-1` | 3 | `M11.NAV1.a` `[x]` · `M11.NAV1.b` `[x]` · `M11.NAV1.c` `[~]` | Manual Windows/Android verification: tab switching, root-tab back behavior, screen reader, and light/dark modes. |

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

### Blocked / backend-deferred (carryover, not Sprint-01 scope)

| Card | Story | Why |
|------|-------|-----|
| Typed API pipeline | `M11.1` (`[~]`) | swaps seeds for the real API later; not needed in the UI phase |
| Challenge verify/exchange + Functions tests | `AUTH-8` (`M11.3c/3d` `[~]`) | server endpoints blocked on backend `M3`; client uses the seed challenge now |

## Critical path

`SEED-1` (interfaces by mid-Week-1) → `SES-6` → `RSVP-8`. `M11.0c` and `NAV-1` run in parallel in
Week 1. Screens start once `SEED-1` + `M11.0c` + `NAV-1` are merged.

## Review evidence and next action

- Verified 2026-06-22: `SouthBaySoccer.Client.Tests` passes in Debug and Release (118/118).
- Windows Debug/Release and Android Debug builds succeed, with existing `NU1903` high-severity
  vulnerability warnings from `SQLitePCLRaw.lib.e_sqlite3` and its Android package.
- Recommended next action: manually verify `NAV-1` on Windows and Android, then continue the
  Sprint 01 closure pass on `SES-6` and `RSVP-8`. Fix the SQLite warning and close the remaining
  UI/test gaps before pulling in stretch `PROF-5` or Sprint 02 work.

## How to keep this current

1. As a task flips `[ ]`→`[~]`→`[x]` in a story's `tasks.md`, move that card to **In progress** /
   **Done** here and tick its task box (☐→☑).
2. Recompute the snapshot points (Done / In progress / To do).
3. A card is **Done** only when its story's Definition of Done in `tasks.md` is met
   (builds, seed-backed `Client.Tests` green, wireframe match, no emoji/raw hex, accessibility + light/dark).
4. Note new blockers under **Blocked** with the reason.
