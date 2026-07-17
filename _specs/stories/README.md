# `_specs/stories/` — Per-story specifications

Each user story has its own directory with the three spec concerns kept **separate**:

```
stories/<STORY-ID>-<slug>/
├── requirements.md   # the user story + Gherkin acceptance criteria (the "what")
├── design.md         # how this story is realized (the "how") — links to global design
└── tasks.md          # the implementation tasks for this story (the "do")
```

## Relationship to the top-level overviews

The top-level `_specs/` files remain the **cross-cutting source of truth** and are not duplicated here:

- [`../requirements.md`](../requirements.md) — personas, **global invariants (`INV-*`)**, **NFRs**, and the epic/story index.
- [`../design.md`](../design.md) — layer/dependency model, domain model, API surface, shared flows, persistence, test mapping.
- [`../tasks.md`](../tasks.md) — the milestone roadmap (`M0…M12`) and dependency graph.

A per-story file states only what is specific to that story and **links** to the global rules it
depends on, rather than copying them (so invariants/NFRs never drift).

## Index

### Authentication (sign-in)

| Story | Directory | Summary |
|-------|-----------|---------|
| `AUTH-7` | [`AUTH-7-welcome-back-screen/`](AUTH-7-welcome-back-screen/requirements.md) | The Welcome Back (sign-in) screen — first app route. |
| `AUTH-8` | [`AUTH-8-continue-with-whatsapp/`](AUTH-8-continue-with-whatsapp/requirements.md) | Phone-number sign-in backed by Pickup Pal lookup; WhatsApp challenge auth is deferred. |
| `AUTH-9` | [`AUTH-9-pickup-pal-actions/`](AUTH-9-pickup-pal-actions/requirements.md) | Pickup Pal bot / signup external actions. |

### UI-first client screens (built against seed data — see [`../design.md`](../design.md) §12)

| Story | Directory | Summary |
|-------|-----------|---------|
| `SEED-1` | [`SEED-1-seed-data-providers/`](SEED-1-seed-data-providers/requirements.md) | Seed-data providers behind client-service interfaces — **build first; unblocks every screen.** |
| `NAV-1` | [`NAV-1-authenticated-shell/`](NAV-1-authenticated-shell/requirements.md) | Authenticated Shell + bottom tabs (Sessions/Stats/Profile); sign-in → Shell. |
| `SES-6` | [`SES-6-sessions-home-screen/`](SES-6-sessions-home-screen/requirements.md) | Sessions (home) — upcoming list, dues status, submit-stats banner. |
| `RSVP-8` | [`RSVP-8-session-detail-screen/`](RSVP-8-session-detail-screen/requirements.md) | Session detail — going + waitlist lists, capacity, RSVP toggle. |
| `PROF-5` | [`PROF-5-player-profile-screen/`](PROF-5-player-profile-screen/requirements.md) | Player profile — career stat tiles, recent form. |
| `LEAD-4` | [`LEAD-4-leaderboard-screen/`](LEAD-4-leaderboard-screen/requirements.md) | Leaderboard — Goals/Assists/Rating/MVP segments. |
| `STAT-7` | [`STAT-7-match-stats-screen/`](STAT-7-match-stats-screen/requirements.md) | Match stats — self-submit goals/assists + captain confirm. |
| `STAT-8` | [`STAT-8-rate-teammates-screen/`](STAT-8-rate-teammates-screen/requirements.md) | Rate teammates — 0–10 rating, like, single MVP. |
| `GDAY-1` | [`GDAY-1-game-day-check-in-tab/`](GDAY-1-game-day-check-in-tab/requirements.md) | Game Day tab - player self-check-in from 7:30 PM to 7:45 PM. |
| `TEAM-4` | [`TEAM-4-captain-assignment-and-draft/`](TEAM-4-captain-assignment-and-draft/requirements.md) | Captain assignment + session-scoped team draft permissions. |
| `STAT-9` | [`STAT-9-captain-approval-and-results/`](STAT-9-captain-approval-and-results/requirements.md) | Post-game captain approval of goals/assists and team result propagation. |
| `ADMIN-4` | [`ADMIN-4-create-session-publish/`](ADMIN-4-create-session-publish/requirements.md) | Admin creates a dated/location-based session and publishes it to the team for RSVP. |

## Recommended execution graph

Two enabling tasks can run in parallel before screen work:

- `SEED-1` owns the complete interfaces, fixtures, mutable demo state, and DI seam.
- `M11.0c` adds the shared control/style extensions required by the wireframes.

Screen stories must not extend Seed clients or create page-local substitutes for missing controls.

```text
SEED-1 + M11.0c
├── Sessions flow: SES-6 → RSVP-8
├── Stats flow:    STAT-7 → STAT-8
├── Discovery:     PROF-5 ─┐
└── Ranking:       LEAD-4 ─┴─ navigation integration
```

After `SEED-1`, run these workstreams in parallel:

1. **Sessions/RSVP workstream (sequential):** `SES-6` then `RSVP-8`. Session detail can be built in
   parallel at the file level, but end-to-end navigation and selected-session handoff are verified
   only after SES-6 exists.
2. **Stats workstream (sequential):** `STAT-7` then `STAT-8`. STAT-8 can be implemented independently
   against a route/match id, but final navigation and same-match context are verified after STAT-7.
3. **Profile/Leaderboard workstream (parallel):** `PROF-5` and `LEAD-4` can be implemented
   independently. Integrate Profile → Leaderboard and Leaderboard → Player Profile routes after
   both pages exist.
4. **Admin/session setup workstream:** `ADMIN-4` can run before game-day operations; it creates the
   player-visible session that `SES-6`, `RSVP-8`, and `GDAY-1` consume.
5. **Game-day operations workstream (sequential after Sessions/Stats):** `GDAY-1` -> `TEAM-4` ->
   `STAT-9`. Check-in must exist before captain selection, captain/team assignment must lock before
   result/stat approval, and recent form must derive from locked results.

Cross-workstream Shell tabs and route names are shared integration points. Assign one owner to
`MauiProgram.cs`, Shell registration, and shared navigation constants to avoid parallel merge
conflicts. Each story otherwise owns its page, page model, and tests.

The remaining backend-oriented stories (WAIV, PAY, CHK, TEAM, NOTIF, ADMIN, and the rest of each
epic) migrate into this structure as they are built.

## Conventions

- Directory name: `<STORY-ID>-<kebab-slug>`.
- A story is **Done** only when every Gherkin scenario has an automated test and works through the
  Function App and, where relevant, the MAUI client (per the global definition of done in `../tasks.md`).
- Visual authority for client stories is [`../../documentation/mobile-wireframes.html`](../../documentation/mobile-wireframes.html); implementation contract is [`../client-ui.md`](../client-ui.md).
