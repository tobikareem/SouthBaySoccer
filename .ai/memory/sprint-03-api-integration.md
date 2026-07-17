---
name: sprint-03-api-integration
description: Sprint 03 wires the MAUI app to the Functions API while preserving wireframe-matched screens
type: project
created: 2026-07-06
---

Sprint 03 is the MAUI API integration sprint, documented in `_specs/sprints/sprint-03-api-integration.md`
with its live board in `_specs/sprints/sprint-03-board.md`. Its focus is typed API clients behind the
existing MAUI service interfaces, not redesigning screens or replacing page-model seams.

**Why:** Earlier planning references used Sprint 03 for admin/game-day operations, but the current
integration need is to run the shipped wireframe-matched MAUI screens against the Azure Functions API.
**How to apply:** For Sprint 03 work, extend API-mode clients and registrations behind interfaces such
as `ISessionsClient`, `IRosterClient`, `IStatsClient`, `ILeaderboardClient`, `ISessionAdminClient`,
and `IGameDayClient`; keep Seed mode working and keep `documentation/mobile-wireframes.html` as the UI
baseline.

Related: [[maui-api-client-pipeline]], [[mobile-wireframes-design-source]], [[client-reusable-ui]]
