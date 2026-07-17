# Sprint 03 API-0 - Contract Inventory and Decisions

**Status:** Done  
**Created:** 2026-07-06  
**Purpose:** Identify the exact MAUI-to-Functions API gaps before implementing Sprint 03 typed clients.

## Summary

API mode currently has a real authentication client and `ApiProfileClient`; every other product-data
interface still resolves to seed providers in non-Release builds. In Release API mode, those remaining
interfaces are not registered yet, so Sprint 03 must add real typed clients before the MAUI product
screens can run end-to-end against the Function App.

The create-session page should be the first vertical slice after API-0. It already has a
wireframe-matched MAUI page and page model. The blocking gap is that the page contract is a
screen-shaped admin workflow (`GetDefaults`, `CreateDraft`, `Publish`, `Update`, managed-session
edit), while the backend currently exposes lower-level scheduling endpoints and `POST /sessions`
creates a published session immediately.

## Current API registration

`SouthBaySoccer/Services/Clients/ClientServiceCollectionExtensions.cs`:

| Interface | API-mode implementation today | Notes |
|-----------|-------------------------------|-------|
| `IAuthenticationClient` | `AuthenticationClient` | Uses anonymous `HttpClient`; covers sign-in/challenge/refresh. |
| `IProfileClient` | `ApiProfileClient` | Covers `GET profiles/me`; `GetProfileAsync(playerId)` returns `null`. |
| `ISessionsClient` | Seed fallback in non-Release | Missing real API client. |
| `IRosterClient` | Seed fallback in non-Release | Missing real API client. |
| `ISessionAdminClient` | Seed fallback in non-Release | Missing real API client; first Sprint 03 target. |
| `IStatsClient` | Seed fallback in non-Release | Missing real API client. |
| `ILeaderboardClient` | Seed fallback in non-Release | Missing real API client. |
| `IPlayersClient` | Seed fallback in non-Release | Missing real API client. |
| `IGameDayClient` | Seed fallback in non-Release | Missing real API client. |

Decision: add concrete API implementations one vertical slice at a time and register them in API
mode. Keep Seed mode as-is for deterministic tests and demos.

## Function route inventory

| Area | Existing routes | Fit for MAUI screen contracts |
|------|-----------------|-------------------------------|
| Auth | `POST auth/pickuppal/phone/sign-in`, `POST auth/whatsapp/challenges`, `POST auth/whatsapp/challenges/verify`, `POST auth/refresh`, `POST auth/sign-out` | Mostly fits; sign-out is not on `IAuthenticationClient` yet. |
| Profile | `GET profiles/me`, `PUT profiles/me`, `POST profiles/guests`, `POST profiles/merges` | Current player profile fits partially. Public/profile-by-id and player directory read APIs are missing. |
| Waiver | `GET waivers/current`, `POST waivers/accept`, `GET waivers/eligibility/me` | Available for future waiver UI wiring. |
| Scheduling | `GET seasons`, `POST seasons`, `GET venues`, `POST venues`, `GET sessions`, `POST sessions`, `POST sessions/{id}/cancel`, recurrence endpoints | Useful base, but does not match `ISessionAdminClient` or player dashboard/detail projections. |
| RSVP/check-in | `POST sessions/{id}/rsvp`, `DELETE sessions/{id}/rsvp`, `GET sessions/{id}/rsvp/me`, admin override, check-in/no-show | RSVP command surface exists; roster read and self check-in projection are missing. |
| Payments | `POST sessions/{id}/payments/drop-in-checkout`, `GET sessions/{id}/payments/eligibility/me` | Fits eligibility/checkout display; Stripe remains server authority. |
| Stats | Match create/events/results/feedback/lock/corrections plus leaderboard and profile stat reads | Leaderboard can wire; match stats/rateable teammate read projections are missing. |
| Game day | Uses pieces from RSVP and Stats routes | Dedicated `GameDayContextDto`, captain assignment, draft, and post-game approval read/write routes are missing. |

## Interface gap matrix

| MAUI interface | Wire as-is? | Required decision/work |
|----------------|-------------|------------------------|
| `IAuthenticationClient` | Yes, mostly | Add sign-out only when the app needs it; otherwise harden tests around current methods. |
| `IProfileClient` | Partially | Compose current profile with `players/me/stats` for career stats, but add `GET profiles/{playerProfileId}` or player-directory support before opening arbitrary profiles from leaderboards. |
| `ISessionAdminClient` | No | Add admin workflow endpoints matching the page contract before `ApiSessionAdminClient`. This is the first implementation slice. |
| `ISessionsClient` | No | Add or expose dashboard/detail projections; `GET sessions` currently returns `SessionAdminResponse[]`, not `SessionsDashboardDto` or `SessionDetailDto`. |
| `IRosterClient` | No | Add `GET sessions/{sessionId}/roster`; RSVP submit/cancel commands can use existing endpoints with `Idempotency-Key`. |
| `ILeaderboardClient` | Yes | `GET stats/leaderboards?seasonId=&metric=&page=&pageSize=` can back the screen. Need current-season selection. |
| `IStatsClient` | Partially | Mutation routes exist; `GetMatchStatsAsync` and `GetRateableTeammatesAsync` need read projections. |
| `IPlayersClient` | No | Add player directory endpoint before replacing `SeedPlayersClient`. |
| `IGameDayClient` | No | Add game-day projection routes and self check-in route/policy before replacing `SeedGameDayClient`. |

## Create Session decision

Implement the create-session slice by aligning the backend to `ISessionAdminClient`, not by adding
business-rule workarounds in MAUI.

Required backend/API contract for `ApiSessionAdminClient`:

| Client method | Proposed route | Notes |
|---------------|----------------|-------|
| `GetDefaultsAsync` | `GET sessions/admin/create-defaults` | Returns `CreateSessionDefaultsDto`; includes `CanManageSessions`, saved/default venue, formats, team options, and local default times. |
| `ListManagedSessionsAsync` | `GET sessions/admin/managed` | Returns `ManagedSessionDto[]`; scoped to sessions the caller can manage. |
| `GetSessionForEditAsync` | `GET sessions/{sessionId}/admin-edit` | Returns `ManagedSessionEditDto` or `404`. |
| `SearchVenuesAsync` | `GET venues?query={query}` | Existing `GET venues` can be extended with optional filtering, returning `VenueResponse[]` mapped to `VenueDto`. |
| `CreateDraftAsync` | `POST sessions/drafts` | Creates `SessionStatus.Draft`; returns `CreateSessionResult`. Do not publish to player feed. |
| `UpdateSessionAsync` | `PUT sessions/{sessionId}` | Updates Draft or Published session fields with audit stamping and validation. |
| `PublishAsync` | `POST sessions/{sessionId}/publish` | Idempotently transitions Draft to Published and returns the same session id on replay. |

Why this route set: `CreateSessionPageModel` already has retry-safe draft/publish behavior. Mapping
`CreateDraftAsync` directly to current `POST /sessions` would create a published session too early,
break the page model's retry semantics, and make the client responsible for hiding backend behavior.

Backend notes:

- The Domain enum already supports `SessionStatus.Draft`; current `CreateSessionCommandHandler`
  always sets `Published`.
- Keep UTC storage in Application/Infrastructure. The API layer can accept the screen command's
  venue-local values only if it has an explicit time-zone conversion rule; otherwise keep request DTOs
  UTC and perform local conversion in `ApiSessionAdminClient` with a documented default venue time zone.
- Mutations that can be retried (`CreateDraft`, `UpdateSession`, `Publish`) should use
  `Idempotency-Key` if duplicate creation or duplicate publish is possible.
- Do not add page-local validation/business rules beyond the existing page model checks; backend
  FluentValidation remains authoritative.

## Follow-on API decisions

After Create Session, implement these in order:

1. **Sessions dashboard/detail and roster projections**
   - Add `GET sessions/dashboard`.
   - Add `GET sessions/{sessionId}` returning `SessionDetailDto`.
   - Add `GET sessions/{sessionId}/roster` returning `RosterDto`.
   - Wire `ISessionsClient` and `IRosterClient`.

2. **Profile/player reads**
   - Complete `ApiProfileClient.GetCurrentProfileAsync()` by composing `GET players/me/stats`.
   - Add `GET profiles/{playerProfileId}` or a player-directory endpoint before wiring
     `GetProfileAsync(playerId)` and `IPlayersClient`.

3. **Stats/leaderboard**
   - Wire `ILeaderboardClient` to existing `GET stats/leaderboards`.
   - Add read projections for `MatchStatsDto` and `RateableTeammateDto` before `IStatsClient`.

4. **Game day**
   - Add dedicated game-day context/captain/draft/post-game projections.
   - Add a self check-in endpoint or adjust check-in policy. Current `POST sessions/{id}/check-ins`
     requires `CanCheckInPlayers` and a `PlayerProfileId`, which does not match player self check-in.

## API-client test requirements

Each typed API client added after API-0 should have fake `HttpMessageHandler` tests for:

- route and HTTP method;
- serialized request body;
- `Idempotency-Key` header for replay-protected mutations;
- `CancellationToken` propagation where observable;
- `404` -> `null` for nullable reads;
- ProblemDetails / `ApiRequestException` mapping to page-model states;
- registration in API mode without breaking Seed mode.

## API-0 outcome

API-0 is complete when this document exists and the Sprint-03 board points the next implementation
step at the Create Session API vertical slice.
