# PROF-5 — Player profile screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the PROF-5 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [ ] **M11.PROF5.a** Implement `ProfilePage` and `ProfilePageModel` directly from the `profile`
  wireframe using the SEED-1 `IProfileClient`: `BrandHeader` (54-dip avatar, name,
  `"Captain" · #8`), the "Linked via WhatsApp" badge +
  "Edit on Pickup Pal" link, `SectionHeader` "Career stats", the three-column `StatTile` grid, the
  "Recent form" `BrandCard` with W/D/L badges, the muted pending note, and the "View season
  leaderboard" `GhostButton`. Bind all values from `IProfileClient`; use only shared brand resources
  and Font Awesome glyphs (no emoji or page-local hex); code-behind is `InitializeComponent` only.
  — Stories: `PROF-5`, `INV-13` · Projects: MAUI client · Depends on: M11.0c, M11.0b.

- [ ] **M11.PROF5.b** Wire `ProfilePageModel` commands: `EditOnPickupPalCommand` (external launcher to the
  configured Pickup Pal account URI; no in-app edit, no stat mutation), `OpenLeaderboardCommand`
  (navigate to the Leaderboard route), and `RefreshCommand`. Drive the `StateView` loading / empty /
  error / offline states and collapse the pending note when absent. External URI via typed config.
  — Stories: `PROF-5` · Projects: MAUI client · Depends on: M11.PROF5.a.

- [ ] **M11.PROF5.c** (PROF-5 slice) `Client.Tests`: the page model loads identity / career stats /
  recent form / pending note from `IProfileClient`; the pending note shows only when reported and is
  hidden otherwise; `EditOnPickupPalCommand` launches the external account URI without an in-app edit
  or stat change; `OpenLeaderboardCommand` navigates to the Leaderboard; loading / empty / error /
  offline states are surfaced and `RefreshCommand` re-requests; icon controls expose semantic
  descriptions and the page stays scrollable and uncut at large text and the narrowest width.
  Build `net10.0-windows10.0.19041.0`.
  — Stories: `PROF-5`, `INV-13` · Depends on: M11.PROF5.b.

**Prerequisites:** M11.0c (shared first-wave UI extensions), M11.0b (seed-data infrastructure / DI).
**Related task slices:** `AUTH-7` (signed-in session, external-launch pattern),
`LEAD-1` (leaderboard destination).

**Done when:** the screen reproduces the `profile` wireframe exactly from shared resources and the
seed `IProfileClient`, all PROF-5 scenarios have passing `Client.Tests`, no emoji/raw hex are used,
and the client builds.
