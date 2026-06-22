# SEED-1 — Seed-data providers · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the SEED-1 slice of milestone **M11**; the full milestone roadmap, the UI-first delivery strategy,
and the dependency graph live in [`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in
progress · `[x]` done.

- [x] **M11.0b** Add seed-data providers in `SouthBaySoccer/SeedData/` implementing the client-service
  interfaces — `IAuthenticationClient` (existing), `ISessionsClient`, `IRosterClient`, `IStatsClient`,
  `ILeaderboardClient`, `IProfileClient` — with one `SeedFixtures` source of deterministic fixtures
  matching every SES-6/RSVP-8/PROF-5/LEAD-4/STAT-7/STAT-8 wireframe value and operation. Add an
  application-scoped, resettable `SeedState` for RSVP/stat/rating commands while keeping
  `SeedFixtures` immutable. Register Seed through `ClientDataSource`; fail fast for unavailable Api
  registrations and for Release+Seed. Guard `SeedData/` out of Release, include no real personal
  data, and perform no network/file/database access. Dependent screen stories consume these
  contracts unchanged and must not add Seed methods or fixtures.
  — Stories: `SEED-1` (UI-first phase, design.md §12) · Projects: MAUI client · Depends on: M11.0.

- [x] **M11.0b-tests** (SEED-1 slice) `Client.Tests`: each `Seed*Client` returns stable baseline
  fixtures across fresh/reset state with no I/O; command changes remain application-scoped, are
  visible to later reads, and do not mutate shared fixtures; the guest "Tunde B." appears with
  the guest flag and no identity link; fixtures expose all values and operations required by the six
  screen stories; Seed resolves the complete Seed implementation set, while unavailable Api and
  Release+Seed configurations fail fast; a guard asserts fixtures hold only invented identifiers.
  Build `net10.0-windows10.0.19041.0`.
  — Stories: `SEED-1` · Projects: MAUI client · Depends on: M11.0b.

  Verified 2026-06-22: Debug and Release client tests pass (110/110); Windows Debug/Release and
  Android Debug builds succeed. Seed registration, reset behavior, complete client contracts,
  invented fixture safety, unavailable-API failure, and Release+Seed rejection are covered.

**Prerequisites:** M11.0 (reusable UI foundation, done). **Enables:** the typed API client swap in
[`M11.1`](../../tasks.md) with no page or page-model change, and the backend-dependent client
scenarios across SES / RSVP / STAT / LEAD / PROF validated against seeds in the UI-first phase.

**Done when:** every client-service interface has a deterministic `Seed*Client`, the active provider
is chosen by `ClientDataSource` configuration with no screen change between Seed and API, seeds are
excluded from Release and contain no real personal data, all SEED-1 `Client.Tests` pass, and the
client builds.
