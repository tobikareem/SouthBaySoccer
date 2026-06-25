# Parallel work plan — Claude × Codex (Sprint 01 closeout)

Two agents work the remaining Sprint 01 stories **in parallel without overlapping files**. Split is by
**vertical story slice** (each agent owns a whole story: page + view-model + seed binding + tests), not
by layer — splitting by layer guarantees collisions on shared files.

## Assignment

| Agent | Stories | Branch | Scope |
|-------|---------|--------|-------|
| **Claude** | `SES-6` (Sessions home) + `RSVP-8` (Session detail / RSVP+waitlist) | `claude/sessions-flow` | The coupled sessions flow — shared sessions seed source + home→detail nav contract, so one owner. |
| **Codex** | `PROF-5` (Player profile) + `chore: SQLite NU1903` | `codex/profile` | Independent profile tab/page + isolated csproj vulnerability bump. |

Both branch off the current integration base and merge back via PR/review. Never both on one branch.

## Shared-file ownership (the only collision risk)

Per-story files are disjoint. These shared files are **pre-allocated** so neither agent blocks the other:

| File | Rule |
|------|------|
| `SouthBaySoccer/MauiProgram.cs` (DI) | Append-only. Each agent adds only its own story's registrations. Profile's page type may already be a placeholder from NAV-1 — Codex edits only the profile line. |
| `SouthBaySoccer/AppShell.xaml` (routes) | Profile route already exists as a NAV-1 placeholder; Codex fills the page behind it, does **not** re-declare the route. Claude touches only `sessions`. |
| `Resources/Styles/Brand*` + control library (`M11.0`/`M11.0c`) | **Frozen.** Reuse existing controls. If a new shared control is genuinely needed, raise it as its own integration commit — do not fork a page-local variant (see CLAUDE.md mobile-design rule). |
| Seed fixtures/state (`SEED-1`) | **Frozen** (immutable `SeedFixtures` + resettable `SeedState`). Add new seed *bindings* in your own client service; do not mutate the fixtures. |
| `_specs/sprints/sprint-01-board.md` | Each agent moves only **its own** cards. Claude keeps the board authoritative; Codex notes status in its story `tasks.md` and Claude reconciles the board. |
| `_specs/stories/<ID>/` | Edit only your own story's dir. |

If a genuinely shared edit is unavoidable, it goes in a separate **integration commit** owned by Claude,
merged before either feature branch rebases on it.

## Definition of Done (both agents, from CLAUDE.md)

Builds clean (`dotnet build`, zero new warnings) · seed-backed `Client.Tests` green · matches
`documentation/mobile-wireframes.html` · Font Awesome glyphs only (INV-13, no emoji/raw hex) ·
accessible (≥44 dip targets, screen-reader labels) · light + dark verified · no PII/secrets in
logs/URLs.

---

## Codex task brief

> Codex already auto-discovers `AGENTS.md` at the repo root and the `skills/` (`brand-design-kit`,
> `maui-conventions`, `player-stats`) — follow them. Start by skimming `.ai/memory/INDEX.md` and
> `.ai/lessons/INDEX.md`.

**Branch:** `codex/profile` off the current base.

### Task A — PROF-5: Player profile screen
- Spec: `_specs/stories/PROF-5-player-profile-screen/{requirements,design,tasks}.md` (authoritative).
- Visual authority: the `data-s="profile"` screen in `documentation/mobile-wireframes.html`.
- Build the Profile page + view-model behind the **existing** NAV-1 `profile` Shell route/placeholder.
- Bind to seed data via the existing client service interface (do **not** edit `SeedFixtures`); add a
  read-only profile query in your own service if one isn't present.
- Render career stat tiles (goals / assists / matches played / win rate) with the `StatTile` control and
  recent form using existing controls. Reuse `M11.0`/`M11.0c` controls only.
- Tests in `SouthBaySoccer.Client.Tests`: profile binds seed data, empty/zero-state renders, stat tiles
  show Font Awesome glyphs with semantic names. Naming `Method_State_Expected`.
- Owns only: `Pages/PlayerProfilePage.*`, its view-model, its client-service binding, its tests, the
  single profile DI line, and `_specs/stories/PROF-5-.../tasks.md`.

### Task B — chore: resolve SQLite `NU1903`
- `SQLitePCLRaw.lib.e_sqlite3` (and its Android transitive package) raise high-severity `NU1903`
  vulnerability warnings on Windows/Android builds.
- Bump the offending package(s) to a non-vulnerable version (touch only the `.csproj` / central package
  versions). Confirm `dotnet build` for `net10.0-windows10.0.19041.0` and `net10.0-android` shows no
  `NU1903`, and the test suite stays green.
- Owns only: the `.csproj` / `Directory.Packages.props` version lines.

### Do NOT touch (Claude owns this branch)
`Pages/SessionsHomePage.*`, `Pages/SessionDetailPage.*`, the sessions client service, sessions nav
(`Services/Navigation/ShellSessionsNavigator.cs`), and `_specs/stories/SES-6-*` / `RSVP-8-*`.

When done, push `codex/profile` and open a PR; Claude reconciles the board and integrates.

---

## Claude lane (for the record)
**Branch:** `claude/sessions-flow`. Stories `SES-6` + `RSVP-8`. Remaining: SES-6 semantic/responsive
automated checks + light/dark device pass; RSVP-8 collapsed going-roster (`+ N more going`) + device
verification. Owns the sessions pages, sessions seed binding, and sessions nav.
