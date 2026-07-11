# Sprint 03 - MAUI API Integration

**Phase:** API wiring for the shipped MAUI product screens  
**Length:** 2 weeks  
**Target TFMs:** `net10.0-windows10.0.19041.0`, `net10.0-android`  
**Board:** [`sprint-03-board.md`](sprint-03-board.md)  
**API-0 inventory:** [`sprint-03-api-0-contract-inventory.md`](sprint-03-api-0-contract-inventory.md)

## Sprint goal

> Run the existing wireframe-matched MAUI app against the Azure Functions API for the core player and
> admin flows, with Seed mode retained only as a deterministic demo/test provider.

Sprint 01 and the stats wave proved the MAUI screen composition against seed data. Backend milestones
M1-M9 now provide the Function App, Contracts project, authentication/session token flow, scheduling,
RSVP/check-in, stats, and leaderboard endpoints. Sprint 03 is the integration sprint: complete the
typed API clients behind the existing MAUI service interfaces, verify the screen behavior in API
mode, and close stale story-spec status where backend work has already landed.

## Design baseline

`documentation/mobile-wireframes.html` remains the visual and interaction baseline for every screen.
API mode must not change screen hierarchy, spacing, component choices, Shell navigation, or green /
white brand treatment. Any required visual change must first update the wireframe, then
`_specs/client-ui.md`, then the shared controls/styles.

Implementation rule: page models continue to depend on `ISessionsClient`, `IRosterClient`,
`IStatsClient`, `ILeaderboardClient`, `IProfileClient`, `ISessionAdminClient`, and `IGameDayClient`.
No page or page model should take a raw `HttpClient` dependency.

## Review of what is done

- **UI foundation and first-wave screens are done.** `NAV-1`, `SES-6`, `RSVP-8`, `PROF-5`,
  `LEAD-4`, `STAT-7`, and `STAT-8` are implemented against seed clients and accepted against the
  mobile wireframe.
- **Admin create-session UI is mostly done.** `ADMIN-4` has the create/edit/publish screen,
  seed-backed client behavior, admin navigation, and client tests.
- **Typed API foundation is done.** `M11.1` registered the API-mode `HttpClientFactory` pipeline:
  `CorrelationIdHandler -> AuthenticationHandler -> ApiExceptionHandler`, plus secure token storage
  and single-flight refresh.
- **One API provider is wired.** `IProfileClient` resolves to `ApiProfileClient` in API mode and
  calls `GET profiles/me`.
- **Backend API surface is broadly present.** The Functions project exposes authentication,
  profile/waiver, scheduling/session, RSVP/check-in, payment eligibility/drop-in checkout, stats
  mutation, and leaderboard endpoints.
- **Backend roadmap status is ahead of some story task files.** `_specs/tasks.md` marks `M6.5`,
  `M7.5`, `M8`, and `M9.1-M9.3` complete, while some per-story task checkboxes still show backend or
  projection slices open.

## What needs to be done

1. Complete API clients for every MAUI service interface currently backed only by seed clients.
2. Add API-mode contract tests with fake `HttpMessageHandler` coverage for routes, payloads,
   idempotency headers, token-refresh retry behavior, and error mapping.
3. Reconcile contract gaps between screen-shaped seed DTOs and backend endpoint responses.
4. Wire local API smoke configuration so the MAUI app can run against the Function App without
   secrets in source.
5. Verify all existing wireframe screens still match the mobile wireframe in API mode.
6. Update stale story task statuses where completed backend work is already proven by the roadmap and
   tests.

## Sprint backlog

| # | Item | Story | Pts | Depends on | Notes |
|---|------|-------|----:|------------|-------|
| 1 | API client inventory and contract-gap cleanup | `API-0` | 3 | M11.1 | Decide whether to add missing read endpoints, compose existing endpoints, or adjust Contracts DTOs. |
| 2 | Auth/session API client hardening | `AUTH-8`, `AUTH-9`, `M11.1` | 3 | M3, M11.1 | Ensure phone sign-in, WhatsApp challenge, refresh, sign-out, and Pickup Pal actions work in API mode. |
| 3 | Sessions, roster, RSVP, payment eligibility API clients | `SES-6`, `RSVP-8`, `PAY-5` | 8 | M6, M7, M11.1 | Wire `ISessionsClient` and `IRosterClient`; keep RSVP as intent, not attendance. |
| 4 | Stats and leaderboard API clients | `LEAD-4`, `STAT-7`, `STAT-8`, `STAT-9` | 8 | M8, M9, M11.1 | Wire `IStatsClient` and `ILeaderboardClient`; projections derive from approved raw facts. |
| 5 | Admin and game-day API clients | `ADMIN-4`, `GDAY-1`, `TEAM-4`, `STAT-9` | 8 | M6.5, M7.5, M8 | Wire create/edit/publish, check-in, captain assignment/draft, post-game approval. |
| 6 | API-mode end-to-end smoke and docs | `M11` | 5 | items 2-5 | Local Function App + MAUI API-mode runbook, smoke checklist, no secrets in source. |
| 7 | Spec status reconciliation | `SPEC-3` | 2 | roadmap review | Update stale story task checkboxes/notes after verifying current backend implementation. |

**Committed:** 37 pts. If capacity is tight, item 5 can split: create-session/check-in first,
captain draft/post-game approval second.

API-0 output: [`sprint-03-api-0-contract-inventory.md`](sprint-03-api-0-contract-inventory.md)
records the current route/interface gaps and the Create Session API contract decision.

## API mapping checklist

| MAUI interface | Current API state | Sprint 03 work |
|----------------|-------------------|----------------|
| `IAuthenticationClient` | Backend auth endpoints exist. | Ensure API provider covers phone sign-in, WhatsApp challenge request/verify, refresh, and sign-out with safe token persistence. |
| `IProfileClient` | `ApiProfileClient.GetCurrentProfileAsync()` exists. | Fill any missing profile/stat/recent-form composition needed by `PROF-5` and Sessions greeting. |
| `ISessionsClient` | `GET sessions`, `POST sessions`, recurrence endpoints exist. | Build dashboard/detail projections from real session, payment eligibility, profile, and stats prompt data. |
| `IRosterClient` | RSVP endpoints exist; roster read contract exists but endpoint coverage must be verified. | Add or compose going/waitlist reads; wire RSVP submit/cancel with idempotency where required. |
| `ILeaderboardClient` | `GET stats/leaderboards` exists. | Map metric segments, pagination defaults, tie-break order, empty/error states. |
| `IStatsClient` | Stats mutation endpoints exist; read shape gaps likely remain. | Wire match stats, submit/confirm, rateable teammates, and peer feedback without leaking raw backend complexity into page models. |
| `ISessionAdminClient` | Scheduling endpoints exist. | Wire defaults, created-session list/edit/update/publish semantics; reconcile story task status for completed M6.5 behavior. |
| `IGameDayClient` | Check-in and stats endpoints exist; dedicated game-day read endpoints may be missing. | Decide whether to add read endpoints for game-day context, captain assignment/draft, and post-game approval or compose existing data safely. |

## Definition of Done

- API mode resolves real typed clients for every committed interface; Seed mode still works.
- Existing MAUI pages and page models remain wireframe-shaped and token-driven: no page-local hex,
  emoji, or raw `HttpClient` dependencies in pages/page models.
- API clients use the shared pipeline, propagate `CancellationToken`, attach idempotency keys on
  mutation endpoints that require replay protection, and map server ProblemDetails to safe
  user-facing states.
- `SouthBaySoccer.Client.Tests` cover API client route/payload/error behavior and existing page-model
  behavior through mocked interfaces.
- A local API-mode smoke run proves: sign in -> Sessions home -> session detail -> RSVP/waitlist ->
  profile -> leaderboard -> stats/rating -> admin create session -> check-in/game-day path.
- `dotnet test` passes for affected tests, and the MAUI client builds for Windows and Android with no
  new warnings.
- No secrets, tokens, phone numbers, payment identifiers, or personal data are committed or logged.

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Backend endpoints return command-oriented contracts that do not match screen projections. | Add narrow read endpoints or API-client composition; do not contort page models or duplicate business rules in MAUI. |
| API mode changes screen layout or copy. | Compare against `documentation/mobile-wireframes.html`; visual changes require wireframe/spec updates first. |
| Story task files disagree with roadmap status. | Reconcile only after checking code/tests, and leave notes where backend is complete but client API wiring remains open. |
| Idempotency is missed on mutations. | API-client tests assert idempotency headers on RSVP, check-in, and publish/update commands that require replay protection. |
| Payment eligibility appears database-authoritative in the client. | Client only displays server-provided eligibility; Stripe webhook state remains authoritative on the backend. |

## Out of scope

- Redesigning screens or replacing the shared control library.
- Reintroducing local SQLite as a source of truth for product data.
- Implementing Stripe as a client-side authority.
- Wholesale sample-app cleanup unrelated to API mode.
