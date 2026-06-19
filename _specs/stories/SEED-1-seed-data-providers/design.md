# SEED-1 — Seed-data providers · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. The cross-cutting UI-first
seed-data strategy (why seeds exist, how they are swapped, and their exclusion rules) is
[`../../design.md`](../../design.md) §12 and is **not duplicated here**; this file specifies the
concrete client-service interfaces the screens depend on, their `Seed*Client` implementations, the
deterministic fixtures, and the configuration-driven dependency injection.

Per §12, page models depend only on client-service abstractions; pages bind to page models; neither
knows whether data is real or seeded. Swapping seeds for the typed API client (`M11.1`) changes only
the registration.

## Client-service interfaces

The interfaces below are the seam between page models and data. Each returns `Contracts` DTOs (or
client view models where no DTO is yet defined), never EF/Stripe/SDK types, so the typed API client
can implement the same contract unchanged. Methods are asynchronous and cancellation-aware.

| Interface | Consumed by | Responsibility |
|---|---|---|
| `IAuthenticationClient` (**already exists**) | AUTH-7/8/9 | Request/verify the WhatsApp sign-in challenge. The seed stub simulates a successful challenge so the authenticated route opens. |
| `ISessionsClient` | SES / RSVP | Sessions-dashboard projection (greeting, dues, featured next match, stats prompt, coming-up list), session detail, and join-waitlist command. |
| `IRosterClient` | RSVP / session detail | Going + ordered waitlist reads and record/withdraw RSVP intent. |
| `IStatsClient` | STAT-7 / STAT-8 | Match-stat entry/confirmation and teammate rating/like/MVP workflows. |
| `ILeaderboardClient` | LEAD-1 | Season leaderboards by axis (Goals / Assists / Rating / MVP). |
| `IProfileClient` | PROF / Profile | Profile identity plus career stat tiles, recent form, and pending-confirmation note. |

Interfaces live with the other client abstractions (alongside `IAuthenticationClient`); only the
implementations are seed-specific.

### Required operation surface

Names may follow the existing client convention, but SEED-1 must deliver this complete semantic
surface before screen work starts:

| Interface | Operations |
|---|---|
| `ISessionsClient` | `GetDashboardAsync(ct)`, `GetSessionAsync(sessionId, ct)`, `JoinWaitlistAsync(sessionId, ct)` |
| `IRosterClient` | `GetRosterAsync(sessionId, ct)`, `SetRsvpIntentAsync(sessionId, isGoing, ct)` |
| `IStatsClient` | `GetMatchStatsAsync(matchId, ct)`, `SubmitStatsAsync(matchId, goals, assists, ct)`, `ConfirmStatsAsync(matchId, playerId, ct)`, `GetRateableTeammatesAsync(matchId, raterId, ct)`, `SubmitRatingsAsync(matchId, raterId, ratings, ct)` |
| `ILeaderboardClient` | `GetRankingAsync(seasonId, metric, ct)` |
| `IProfileClient` | `GetProfileAsync(playerId, ct)`; the current-player route supplies the signed-in player id |

Command result types distinguish success from recoverable failure so page models can revert
optimistic updates. Read DTOs are immutable snapshots and carry stable `Guid` ids, UTC timestamps,
and semantic state; they do not carry UI controls, colors, glyph literals, or commands.

## `Seed*Client` implementations

Implementations live under `SouthBaySoccer/SeedData/`:

```text
SouthBaySoccer/SeedData/
├── SeedFixtures.cs            # one source of truth for all fixture instances (static, immutable)
├── SeedState.cs               # application-scoped mutable demo state, reset from SeedFixtures
├── SeedAuthenticationClient.cs
├── SeedSessionsClient.cs
├── SeedRosterClient.cs
├── SeedStatsClient.cs
├── SeedLeaderboardClient.cs
└── SeedProfileClient.cs
```

Rules for every `Seed*Client`:

- **Deterministic** — returns instances drawn from `SeedFixtures`; identical on every run and device
  before a demo command changes state. No randomness or device-clock-dependent ordering; relative
  labels are fixture copy tied to fixed UTC timestamps.
- **State-safe** — `SeedFixtures` is immutable. RSVP, stat, confirmation, and rating commands update
  an application-scoped `SeedState` initialized from those fixtures. Reset/restart restores the same
  baseline, so tests do not leak state across cases.
- **No I/O** — no network, file, or database access. Methods complete synchronously behind a
  `Task`/`ValueTask` (a small fixed delay may be used only to exercise loading states; default none).
- **No real PII** — names, numbers, and identifiers are invented. The seed WhatsApp challenge accepts
  any input and reports success; it never contacts Pickup Pal.
- **`Guid` identifiers are stable constants** so cross-fixture references (a roster entry → a player,
  a leaderboard row → a player) resolve consistently.
- **Release-guarded** — the `SeedData/` folder compiles only outside Release (build-configuration
  guard / conditional compilation), so seeds never ship as product behavior.

## Configuration-driven dependency injection

A single typed option selects the active provider; registration is the only place that differs
between Seed and API.

```text
ClientDataSource = Seed | Api          # typed option, default Seed in the UI-first phase
```

At composition (through one client-registration extension called by `MauiProgram.cs`):

- `ClientDataSource.Seed` → register each `Seed*Client` against its interface.
- `ClientDataSource.Api` → register each typed API client (`M11.1`) against the same interface once
  that complete set exists. Until then, configuration validation fails fast and lists the missing
  registrations; it must not silently fall back to Seed.

Both branches register the identical interface set, so the container shape — and therefore every page
model constructor — is unchanged. The `Api` branch is the no-screen-change swap the requirements
demand. In Release, the `Seed` branch is unavailable and selecting it is a startup configuration
error; Release never silently changes providers.

## Deterministic fixtures

`SeedFixtures` holds these, matching the mobile-wireframe sample data:

**Players** — a fixed set with stable `Guid`s, display names, positions, and avatar initials,
including the guest **"Tunde B."** carrying `IsGuest = true` and no identity link. Every roster,
leaderboard, and stat fixture references this player set by id.

**Sessions dashboard** (`ISessionsClient`) — one aggregate matching the `home` wireframe:

- group label `Saturday crew`, greeting `Good morning, Tobi`, and dues badge `Paid`;
- featured next match `Marina Field · Saturday pickup`, `Jun 20`, `9:00 AM`, `7v7`,
  `You're going`, and `16 / 20`;
- stats prompt `Submit your latest stats` with
  `2 goals entered · captain confirmation pending`;
- section label `Coming up` and action `See schedule`;
- coming-up card `Stanford Turf · 5v5`, full at `20 / 20`, with waitlist count `3`.

**Session details** (`ISessionsClient`) — fixed detail projections:

| Session | Venue · format | Capacity | State |
|---|---|---|---|
| Marina Field | Marina Field · 7v7 | 16 / 20 going | open, RSVP available |
| Stanford Turf | Stanford Turf · 5v5 | 20 / 20 | full, 3 on the waitlist |

**Rosters** (`IRosterClient`) — per session, a **going** list and an ordered **waitlist**, each entry
with a player reference and position. Marina Field has 16 going and waitlist headroom; Stanford Turf
is full with a 3-entry ordered waitlist (positions 1–3).

**Leaderboards** (`ILeaderboardClient`) — one ranked projection per axis: **Goals**, **Assists**,
**Rating**, **MVP**. Each is an ordered list of player references with the axis value; ties resolve
deterministically so order is stable.

**Match stats** (`IStatsClient`) — current-player totals `2 goals / 1 assist`; teammate submissions
for Jide D. (confirmed), Sade M. and Tunde B. (unconfirmed); submit and confirm commands update only
`SeedState`.

**Rate teammates** (`IStatsClient`) — Kola T. (`2 goals`, rating 9), Jide D. (`1 assist`, rating 7,
liked), and Sade M. (`clean sheet`, rating 8, selected MVP). The signed-in player is excluded.

**Profile** (`IProfileClient`) — current-player identity (`Tobi Kareem`, `"Captain" · #8`, `TK`),
the pending note `2 goals from Sat awaiting confirmation`, recent form `W / W / D / W / L`, and
these career tiles:

| Tile | Value |
|---|---|
| Matches | 24 |
| Goals | 12 |
| Assists | 9 |
| Avg rating | 7.8 |
| MVP | 3 |
| Likes | 41 |

All copy and pictograms surfaced from fixtures follow `INV-13` (Font Awesome glyph + semantic text,
no Unicode emoji) when rendered by the consuming screens; fixtures carry data, not icon literals.

## Test design (`Client.Tests`) — SEED-1 slice

- each `Seed*Client` starts from the same immutable fixtures across test runs (deterministic, stable
  ordering, no I/O);
- command tests prove RSVP/stat/rating changes are visible through later reads, isolated to one
  `SeedState`, and resettable without mutating `SeedFixtures`;
- the guest "Tunde B." is present in the roster fixtures with the guest flag and no identity link;
- session fixtures expose "Marina Field · 7v7" at 16/20 going and "Stanford Turf · 5v5" full with a
  3-entry ordered waitlist;
- leaderboard fixtures expose the four axes (Goals/Assists/Rating/MVP) in stable order;
- match-stat and teammate-rating fixtures expose every operation required by STAT-7/STAT-8;
- profile fixtures expose Matches 24 / Goals 12 / Assists 9 / Avg rating 7.8 / MVP 3 / Likes 41,
  recent form W/W/D/W/L, and the pending note;
- **config validation**: Seed resolves the complete Seed implementation set; unavailable Api
  registrations and Release+Seed fail fast. M11.1 later tests Api parity against the same interfaces;
- fixtures contain no real personal data (a guard asserting invented identifiers only).
