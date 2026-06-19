---
name: ui-first-seed-data
description: UI-first delivery — build MAUI/XAML against seed data; backend (M1–M10) deferred
type: project
created: 2026-06-18
---

Current delivery strategy is **UI-first**: build the .NET MAUI/XAML client before the backend. The
Function App, web services, and database (milestones M1–M10) are deferred. Anything that needs the
backend is satisfied by a **seed-data provider** behind a client service interface (e.g.
`IAuthenticationClient`, `ISessionsClient`), registered by configuration and later swapped for the
typed API client (M11.1) with no page/page-model change. Seeds live in `SouthBaySoccer/SeedData/`,
are Release-guarded, and carry no real personal data.

`SEED-1` owns the complete first-wave interface surface and fixtures. Immutable `SeedFixtures`
provides the deterministic baseline; application-scoped, resettable `SeedState` holds RSVP, stats,
confirmation, and rating changes during a demo run. Screen stories consume this surface unchanged
and do not add Seed methods or fixtures.

**Why:** Lets the team build and demo screens from the wireframes now without waiting on the backend.
**How to apply:** Page models depend on interfaces only; provide a `Seed*Client` with fixtures matching
the wireframe sample data; validate backend-dependent scenarios against seeds now and re-run
server-side verification when each backend milestone lands. Full detail in `_specs/design.md` §12.

Related: [[spec-driven-development]], [[client-reusable-ui]]
