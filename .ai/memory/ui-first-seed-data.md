---
name: ui-first-seed-data
description: API integration phase with Seed mode retained for deterministic demos/tests
type: project
created: 2026-06-18
---

The project began UI-first, with the .NET MAUI/XAML client built against seed data while the backend
was still coming online. Backend features are now substantially implemented through M9, and the
current delivery strategy is API integration: keep Seed mode for deterministic demos/tests while
wiring the same client service interfaces (e.g. `IAuthenticationClient`, `ISessionsClient`) to typed
Functions API clients screen by screen. Seeds live in `SouthBaySoccer/SeedData/`, are
Release-guarded, and carry no real personal data.

`SEED-1` owns the complete first-wave interface surface and fixtures. Immutable `SeedFixtures`
provides the deterministic baseline; application-scoped, resettable `SeedState` holds RSVP, stats,
confirmation, and rating changes during a demo run. Screen stories consume this surface unchanged
and do not add Seed methods or fixtures.

**Why:** Keeps wireframe-matched screens stable while API mode catches up, and preserves deterministic
demo/test data when the backend is unavailable.
**How to apply:** Page models depend on interfaces only; provide a `Seed*Client` with fixtures matching
the wireframe sample data; for API work, add typed clients behind the same interfaces rather than
changing pages/page models. Re-run server-side verification as each API slice lands. Full detail in
`_specs/design.md` §12.

Related: [[spec-driven-development]], [[client-reusable-ui]]
