# SouthBaySoccer — Design

How the [requirements](requirements.md) are realized on the architecture defined in
[`documentation/architecture.md`](../documentation/architecture.md). The architecture document is
**authoritative**; this file maps requirements → layers, components, data, contracts, and flows, and
records the current-state gap. It does not re-decide architecture.

For MAUI product design, [`documentation/mobile-wireframes.html`](../documentation/mobile-wireframes.html)
is the authoritative visual and interaction reference. [`client-ui.md`](client-ui.md) defines how
that reference is implemented through reusable tokens, styles, and controls.

## 1. Current state (gap analysis)

| Area | Current state | Target |
|------|---------------|--------|
| Solution | `SouthBaySoccer.slnx` with `src/` (Contracts, Domain, Application, Infrastructure, Functions) and `tests/` (5 projects) scaffolded and building | Implemented per architecture |
| Client | `SouthBaySoccer/` is the original MAUI **Project/Task sample** with a completed reusable brand UI foundation (`Resources/Styles/Brand*.xaml`, `Controls/`, UI Library showcase) | Product screens in `src/SouthBaySoccer.Client/` calling the API via typed clients and composing the shared design system |
| Domain | `BaseEntity`, payment/role/RSVP enums, generic repository and unit-of-work interfaces | Remaining entities/value objects/invariants/events from architecture §12 |
| Application | Ports exist for current user, Identity, tokens, payments, email/SMS, maps, and time; no use cases or validators | Use cases + FluentValidation + result types |
| Infrastructure | EF Core `DbContext`, `UnitOfWork`, and Azure SQL DI registration exist; no entity configurations, audit interceptor, Identity, repositories, or provider adapters | Complete EF Core mappings, Identity, repositories, Stripe, messaging, maps, outbox |
| Functions | `Program.cs`, `host.json`, `local.settings.json.example` | Middleware pipeline + capability-grouped HTTP/timer/queue functions |
| Tests | `PlaceholderTests.cs` per project | Real unit/integration tests for §17 scenarios |
| Persistence | Azure SQL provider registration and empty `DbContext`; no entities/migrations; client uses local SQLite sample | Azure SQL + EF Core; client SQLite only as optional cache |
| CI | No `.github/workflows` pipeline or analyzer/format gate | Build, test, analyzers, formatting, and secret checks |

**Migration stance (architecture §18):** keep the MAUI sample running; introduce soccer features
behind the API incrementally; the client project move to `src/SouthBaySoccer.Client/` is a dedicated,
separate change. No wholesale rewrite.

## 2. Layer & dependency model

```
Domain  ←  Application  ←  Infrastructure
                 ↑               ↑
                 └──── Functions ┘            Client → Contracts (only)
```

- **Domain** — entities, value objects, enums, domain events, domain services, repository interfaces. No EF/Functions/UI deps.
- **Application** — use-case features, FluentValidation, ports (`ICurrentUser`, `IIdentityService`, `ITokenService`, `IPaymentGateway`, `IEmailService`, `ISmsService`, `IMapsService`, `IClock`), result types. Repository/unit-of-work interfaces live in `Domain/Interfaces/Repositories`.
- **Infrastructure** — EF Core `SouthBaySoccerDbContext`, `IEntityTypeConfiguration<T>` per entity, repositories, `UnitOfWork`, Identity stores, Stripe/SendGrid/Twilio/Azure Maps adapters, outbox.
- **Functions** — composition root + middleware (correlation → exception → authn → authz → execute); HTTP/timer/queue triggers grouped by capability; no business rules.
- **Client (MAUI)** — Pages → PageModels → typed API services → `HttpClient` pipeline (`CorrelationIdHandler` → `AuthenticationHandler` → `ApiExceptionHandler`); secure token storage; depends on `Contracts` only.

## 3. Requirement → component map

| Epic | Domain | Application `Features/` | Functions group | Contracts | Providers |
|------|--------|--------------------------|-----------------|-----------|-----------|
| AUTH | `ApplicationIdentityUser`(infra), refresh-token records | `Authentication` | `Authentication` | `Authentication` | Identity, `ITokenService` |
| PROF | `PlayerProfile`, `EmergencyContact`, `ProfileMerge` | `Players` | `Players` | `Players` | — |
| WAIV | `WaiverDocument`, `WaiverAcceptance` | `Players`/`Compliance` | `Players` | `Players` | — |
| PAY | `Membership`, `PaymentLedger`, `StripeCustomerReference`, `ProcessedWebhookEvent` | `Payments` | `Payments`, `Webhooks` | `Payments` | `IPaymentGateway` (Stripe) |
| SES | `Season`, `Venue`, `RecurrenceRule`, `Session` | `Sessions` | `Sessions`, `Maintenance` (timer) | `Sessions` | `IMapsService` |
| RSVP | `RsvpResponse`, `WaitlistEntry`, `CheckIn` | `Rsvps` | `Rsvps` | `Rsvps` | — |
| CHK | `CheckIn` | `Rsvps`/`Sessions` | `Sessions` | `Sessions` | — |
| TEAM | `Match`, `MatchTeam`, `TeamAssignment`, `MatchResult` | `Stats` | `Sessions`/`Stats` | `Stats` | — |
| STAT | `PlayerMatchStats`, `MatchEvent`, `PlayerRatingVote`, `PlayerLike`, `MatchAward`, `StatCorrection` | `Stats` | `Stats` | `Stats` | — |
| LEAD | (read projections) | `Stats` queries | `Stats` | `Stats` | (optional Table Storage) |
| NOTIF | outbox message | `Messaging` | `Maintenance` (queue) | — | SendGrid, Twilio |
| ADMIN | — | cross-feature queries | per capability | `Common` | App Insights |

## 4. Domain model (from architecture §12)

All entities inherit `BaseEntity` (`Guid Id`, `CreatedAt/By`, `UpdatedAt/By`, `IsDeleted`; UTC).
Key aggregates and invariants:

- **PlayerProfile** is the stats anchor and is **login-optional** (`IsGuest`, no Identity user for guests). `EmergencyContact`, `ProfileMerge` support it. (PROF-3/4)
- **Scheduling**: `Season` (explicit, INV-5) → `Session` (belongs to one Season) with `Venue`,
  `RecurrenceRule`, capacity, and deadline. `RsvpResponse`/`WaitlistEntry` capture attendance intent;
  `CheckIn` and attendance outcome capture actual attendance separately. (SES, RSVP, CHK)
- **Payments**: `Membership`, `PaymentLedger`, `StripeCustomerReference`, `ProcessedWebhookEvent` (unique event ID). Stripe is authority (INV-1). (PAY)
- **Compliance**: `WaiverDocument` (versioned), `WaiverAcceptance`. (WAIV)
- **Stats** at the `Match` grain: `Match` → `MatchTeam` → `TeamAssignment` (per-match link, INV-9); `PlayerMatchStats` (one per participant incl. guests); `MatchEvent` (Goal/Assist/OwnGoal/Yellow/Red); `PlayerRatingVote` (0–10, unique `(MatchId,Voter,Rated)`, no self-vote, INV-8); `PlayerLike`; `MatchAward` (MVP); `MatchResult`; `StatCorrection`. (TEAM, STAT)
- **No** club/coach/competition/league-standing concepts.
- Derived-on-read only (INV-7): G/A/MP, average rating, likes, MVP counts. Leaderboards are query projections (architecture §11), never maintained tables.
- **Domain events**: `PlayerWaitlistPromoted`, `SessionCapacityReached`, `PaymentStatusChanged`, persisted via outbox in the same transaction.

Identity note: `ApplicationIdentityUser : IdentityUser<Guid>` is an **Infrastructure** persistence type linked by `Guid` to `PlayerProfile`; it does not inherit `BaseEntity`.

## 5. API surface (Contracts + Functions, `/api/v1`)

Indicative endpoints (all `[RequirePolicy]` unless noted):

- `Authentication`: `POST /auth/register` `[AllowAnonymous]`, `/auth/confirm-email` `[AllowAnonymous]`, `/auth/sign-in` `[AllowAnonymous]`, `/auth/refresh` `[AllowAnonymous]`, `/auth/password-reset*` `[AllowAnonymous]`, `/auth/sign-out`.
- `Players`: `GET/PUT /players/me`, emergency contact, `POST /players/guests` (`CanManagePlayers`), `POST /players/merge`.
- `Compliance`: `GET /waivers/current` , `POST /waivers/accept`.
- `Payments`: `POST /payments/checkout`, `GET /payments/history`.
- `Webhooks`: `POST /webhooks/stripe` `[AllowAnonymous]` (signature-verified).
- `Sessions`: CRUD seasons/venues/sessions (`CanManageSessions`), `GET /sessions` (upcoming), `GET /sessions/{id}/roster`.
- `Rsvps`: `POST /sessions/{id}/rsvp`, `DELETE …/rsvp`, `GET /sessions/{id}/rsvp/me` (idempotency key required for side-effecting calls).
- `Stats`: matches, team assignment, events, rating votes, likes, awards, corrections; `GET /stats/leaderboard?seasonId=` and `GET /players/{id}/stats` (paginated read projections).

DTOs live in `SouthBaySoccer.Contracts`; the Application layer never returns EF/Stripe/SDK types.
Errors use RFC 7807 `ProblemDetails` per the architecture §7 status table.

## 6. Key flows

**Auth + token refresh (AUTH-3/4):** client posts credentials → `ITokenService` issues access+refresh → `AuthenticationHandler` attaches bearer, refreshes once on expiry with a single in-flight refresh, replays only idempotent requests; reuse of a consumed refresh token revokes the family atomically.

**RSVP + waitlist (RSVP-1..4, INV-2/3):** `SubmitRsvp` validates waiver + payment eligibility, then a **serializable** transaction scoped to the session checks capacity, inserts `RsvpResponse` or `WaitlistEntry` (unique active-RSVP constraint + `rowversion`), with bounded retry → 409. Cancellation runs `PromoteWaitlist` in the same transactional pattern and raises `PlayerWaitlistPromoted` via outbox.

**Stripe (PAY-1/2, INV-1):** client requests checkout → Function creates Checkout server-side → returns short-lived URL → user pays on Stripe → signed webhook → verify signature on raw body → insert event ID (unique) + update ledger/membership atomically → 2xx; duplicate = 2xx no-op; older event does not overwrite newer state.

**Recurring sessions (SES-3):** timer trigger generates occurrences with a deterministic occurrence key + unique DB constraint so retries/scaled runs cannot duplicate.

**Stats + leaderboards (STAT, LEAD, INV-6/7/8):** raw rows recorded per `Match`; `Features/Stats` query handlers aggregate across `Match → Session → Season`; tie-breakers Goals → fewer appearances → Assists; rating = integer-0–10 average of votes received, shown from first vote, zero-vote matches excluded.

**Notifications (NOTIF, §15):** business transaction writes state + outbox message atomically; idempotent dispatcher publishes to queue; queue consumers send via SendGrid/Twilio idempotently with dead-lettering.

Before M10, features may satisfy their transactional requirement by writing an outbox message in
the same database transaction. Actual provider delivery, retry, and dead-letter processing are
implemented in M10; earlier feature acceptance tests assert the durable outbox record.

## 7. Authorization & validation

- **Policies:** `CanManagePlayers`, `CanManageSessions`, `CanCheckInPlayers`, `CanAssignTeams`, `CanRecordStats`, `CanViewFinancialStatus`. Use cases request policies, not role names. Endpoints declare exactly one of `[RequirePolicy]`/`[AllowAnonymous]` (INV-11); `EndpointPolicyResolver` + authz middleware fail closed.
- **Validation (3 levels):** client presentation validation (UX), Application FluentValidation (authoritative), Domain invariants/value objects. No FluentValidation/data annotations on Domain entities.

## 8. Persistence rules (architecture §13)

`SouthBaySoccerDbContext` = unit of work; one `IEntityTypeConfiguration<T>` per entity; global `IsDeleted == false` filter on mutable entities; audit interceptor via `ICurrentUser`+`IClock`; soft delete in app code. Immutable security/operational records (processed webhook IDs, refresh-token history, audit, outbox) are exempt from soft-delete and have explicit retention/purge. Repositories are aggregate/use-case specific; never expose `IQueryable`; large tables always filtered + paginated. Migrations run as a controlled deploy step (not on cold start).

## 9. Test mapping (architecture §17)

| Test project | Covers (stories) |
|--------------|------------------|
| `Domain.Tests` | invariants: rating votes (STAT-3), stat aggregation (LEAD-1), team/match grain (TEAM-2), waiver/eligibility rules |
| `Application.Tests` | use cases with Moq: RSVP/waitlist (RSVP-1..7), payments (PAY-*), profile merge (PROF-4) |
| `Infrastructure.Tests` | EF mappings, query filters, concurrency, transactions, Identity stores, webhook idempotency (PAY-2) |
| `Functions.Tests` | middleware, fail-closed authz (NFR-AuthZ), JWT (AUTH-3/4), webhook signature, ProblemDetails |
| `Client.Tests` | page models, API client, token refresh (AUTH-4), error/offline states (ADMIN-2) |

Naming: `MethodName_StateUnderTest_ExpectedBehavior`. Required high-risk scenarios from §17 are
first-class acceptance tests, traced to the story IDs in `requirements.md`.

## 10. Decision log and open decisions

- **Resolved — membership model:** support both monthly membership eligibility and
  session-specific guest/drop-in eligibility. They are separate ledger/eligibility concepts;
  neither is manually marked paid.
- Whether SMS (Twilio) ships in v1 or later (cost + A2P 10DLC registration).
- Team-balancing algorithm for TEAM-2 (manual vs. rating-weighted auto-balance).
- Minimum minutes threshold for a goalkeeper clean sheet, scaled to session length.
