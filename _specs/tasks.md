# SouthBaySoccer � Tasks & Sequential Roadmap

Ordered, dependency-aware implementation plan. Tasks trace to stories in
[requirements.md](requirements.md) and components in [design.md](design.md), and follow the
incremental adoption order in [`documentation/architecture.md`](../documentation/architecture.md) Â§18.

**Conventions**
- Task ID `M<milestone>.<n>`. Each task lists **Stories**, **Projects**, **Depends on**, and **Done when**.
- "Done when" always includes: affected backend projects build, relevant test projects pass, the
  affected MAUI target builds when client code changes, new behavior is covered by tests, no new
  warnings, and no secrets are committed.
- Work one milestone at a time; each milestone leaves the solution buildable (architecture Â§18). Don't mix sample removal with new feature behavior.
- Status: `[ ]` todo Â· `[~]` in progress Â· `[x]` done.

---

## Delivery strategy â€” UI-first (current)

The current focus is the **MAUI/XAML client**. Backend milestones **M1â€“M10** (Domain, Application,
Infrastructure, Functions, Azure SQL) are **deferred**. Client stories that need data use **seed-data
providers** (`M11.0b`) behind client service interfaces, swapped for the typed API client (`M11.1`)
later with no screen change. During this phase, client tasks are **not** blocked on `M3`; backend
wiring is completed in the backend phase. See `design.md` Â§12.

## Sequential roadmap (high level)

1. **M0 Foundation** â€” shared kernel, `BaseEntity`, cross-cutting abstractions, CI.
2. **M1 Persistence & Identity core** â€” EF Core `DbContext`, audit/soft-delete, ASP.NET Identity stores, migrations.
3. **M2 Functions pipeline** â€” middleware (correlation/exception/authn/authz), fail-closed classification, ProblemDetails.
4. **M3 Phone Number Session Authentication** - Pickup Pal phone lookup, JWT/session issuance, refresh rotation, and backend auth authority. WhatsApp challenge/link authentication, email/password registration, confirm-email, sign-in, and password reset flows are explicitly out of current scope.
5. **M4 Player profiles & waivers** â€” profiles, guests, emergency contact, waiver accept + gating (PROF, WAIV).
6. **M5 Payments** â€” Stripe checkout + idempotent webhooks + membership/ledger (PAY).
7. **M6 Seasons, venues & sessions** â€” seasons, venues, recurring sessions timer (SES).
8. **M7 RSVP & waitlist** â€” eligibility-gated RSVP, transactional capacity, waitlist promotion (RSVP, CHK).
9. **M8 Teams, matches & stats** â€” matches, per-match teams, events, ratings, likes, MVP, corrections (TEAM, STAT).
10. **M9 Leaderboards & queries** â€” season/career read projections, tie-breakers (LEAD).
11. **M10 Notifications & reliability** â€” outbox dispatcher, SendGrid/Twilio, reminders (NOTIF).
12. **M11 Client integration** â€” typed API clients, token handling, replace one sample flow, offline live-stats (ADMIN-2), then client move to `src/SouthBaySoccer.Client/`.
13. **M12 Hardening & deploy** â€” observability, rate limiting, Key Vault/managed identity, deployment topology, controlled migrations.

---

## M0 â€” Foundation
- [~] **M0.1** Define `BaseEntity`, common value objects, `IClock`, `ICurrentUser`, result/error types. â€” Current: `BaseEntity`, `IClock`, `ICurrentUser`, and transport `ApiError` exist; common value objects, an Application result type, and real `Domain.Tests` remain. â€” Stories: INV-10 Â· Projects: Domain, Application.
- [~] **M0.2** Add domain enumerations (roles, event types, payment/membership status, RSVP intent status, attendance outcome). â€” Current: role/payment/RSVP intent enums exist; event types and attendance outcome remain. â€” Projects: Domain.
- [ ] **M0.3** Establish CI: build backend projects, run all layer-specific test projects, build the
  Windows MAUI TFM, run analyzers and `dotnet format`. â€” Done when: pipeline green on the skeleton.
- [~] **M0.4** Confirm decisions in design.md Â§10. â€” Current: membership is resolved as monthly membership plus session-specific guest/drop-in eligibility; SMS timing, balancing, and clean-sheet threshold remain. â€” Output: decisions recorded in design.md and durable conventions mirrored in `.ai/memory/`.

## M1 â€” Persistence & Identity core
- [x] **M1.1** `SouthBaySoccerDbContext` + `IUnitOfWork`; audit-field save interceptor (`ICurrentUser`+`IClock`); global `IsDeleted==false` filter. - Done: EF Core context, unit of work, LocalDB-backed schema, global soft-delete filters, audit/soft-delete save interceptor, and Infrastructure tests are in place. - Projects: Infrastructure; Depends on: M0.1.
- [x] **M1.2** `ApplicationIdentityUser : IdentityUser<Guid>`, `AddIdentityCore`, role/token providers, EF Identity stores; shared Data Protection keys. - Done: Identity Core is registered with GUID users/roles, EF stores, default data-protection token provider, EF-backed Data Protection keys, and an `IIdentityService` adapter with Infrastructure tests. - Stories: AUTH-*; Projects: Infrastructure.
- [x] **M1.3** Refresh-token records (hashed, revocable, rotation/reuse, family revoke) + `ProcessedWebhookEvent`, outbox tables - exempt from soft-delete with retention. - Done: refresh-token persistence supports rotation metadata, reuse detection, family revocation references, device/session hashes, SQL constraints, and immutable operational-table tests for refresh tokens, webhooks, and outbox. Token exchange behavior remains in M3.3. - Stories: AUTH-4, PAY-2, NOTIF-1.
- [x] **M1.4** First migration; document controlled (non-cold-start) migration runner. - Done: EF migrations exist, `_specs/controlled-migrations.md` documents script generation/application with a deployment identity, Infrastructure tests apply migrations against isolated SQL-compatible infrastructure, and Functions tests guard against cold-start schema mutation.

## M2 â€” Functions pipeline
- [x] **M2.1** Middleware order: correlation -> exception -> authentication -> authorization -> execute. - Done: `Program.cs` registers `AddSouthBaySoccerHttpPipeline`, middleware is composed in spec order, and `HttpPipelineOrderTests` verifies the exact sequence.
- [x] **M2.2** `EndpointPolicyResolver`, `[RequirePolicy]`/`[AllowAnonymous]`, fail-closed rejection of unclassified/conflicting endpoints. - Done: reflection resolver, endpoint metadata attributes, authorization middleware, and resolver tests cover anonymous, policy, missing, conflicting, and empty-policy classifications.
- [x] **M2.3** RFC 7807 `ProblemDetails` exception mapping (400/401/403/404/409/429/500); correlation-ID logging; no sensitive data in responses/logs. - Done: `ProblemDetailsMapper` maps required statuses, exception middleware writes safe problem responses with correlation IDs, correlation IDs are accepted/generated safely, and tests cover status mapping plus sensitive-detail suppression.
- [x] **M2.4** `Functions.Tests`: classification, authz, ProblemDetails, correlation. - Done: resolver tests cover fail-closed classification, authorizer tests cover anonymous/unauthenticated/forbidden/authorized paths with Moq, ProblemDetails tests cover required status mappings and safe unexpected errors, and correlation tests cover accepted/generated IDs.

## M3 - Phone Number Session Authentication

Email/password registration, confirm-email, sign-in, and password reset flows are explicitly out of
Sprint 02 scope. The current backend authentication authority is a submitted phone number confirmed
against Pickup Pal, followed by server-issued access and refresh tokens. WhatsApp challenge/link
authentication is deferred.

- [x] **M3.1** `ITokenService` for JWT issue/validate, access-token claims, and signing-key rotation
  via configuration/Key Vault overlap. - Done: `JwtTokenService` issues and validates HMAC JWTs with `kid`, issuer/audience, roles, policies, expiry, and retired-key validation tests. ï¿½ Stories: AUTH-3.
- [x] **M3.2** `Features/Authentication`: phone-number sign-in Application use cases with
  FluentValidation. - Done: direct phone sign-in validates and masks phone input, confirms the phone through Pickup Pal, syncs identity/profile records, and issues local SouthBaySoccer tokens. Legacy WhatsApp challenge handlers remain deferred/legacy seams, not the current sign-in model. - Stories: AUTH-8, AUTH-3.
- [x] **M3.3** Refresh-token exchange with atomic rotation, reuse detection, and family revocation.
  Done: EF-backed serializable rotation service hashes raw tokens, consumes/replaces active tokens, marks reuse, and revokes token families. Client-side single-flight refresh is implemented in M11.1. ï¿½ Stories: AUTH-4.
- [x] **M3.4** HTTP Functions + Contracts for Pickup Pal phone sign-in and refresh-token exchange
  (`[AllowAnonymous]` where intended). - Done: `POST /auth/pickuppal/phone/sign-in` confirms the submitted phone through Pickup Pal, syncs local identity/profile data, issues SouthBaySoccer access and refresh tokens, and refresh-token rotation issues fresh JWT access tokens. WhatsApp challenge request/verify endpoints are deferred/legacy compatibility and are not the current product sign-in path. - Stories: AUTH-3, AUTH-4, AUTH-8.

## M4 â€” Player profiles & waivers
- [x] **M4.1** `PlayerProfile` (+`IsGuest`), `EmergencyContact`; configs + repository. - Done: entities/configs exist, profile/waiver repositories are registered, and schema tests remain green. - Stories: PROF-1,2,3.
- [x] **M4.2** `Features/Players`: get/update me, create guest, and create an auditable
  `ProfileMerge` link/retirement workflow. - Done: handlers validate profile updates, store private emergency-contact hashes/masks, create guest profiles without Identity users, and create completed profile-merge audit records while retiring the guest profile. Stat reassignment remains deferred to M8.6. - Stories: PROF-1,3,4.
- [x] **M4.3** `WaiverDocument` (versioned) + `WaiverAcceptance`; accept use case; eligibility helper. - Done: current published waiver query, idempotent current-waiver acceptance, and current-waiver eligibility query are implemented and tested. - Stories: WAIV-1,2,3.
- [x] **M4.4** Endpoints + contracts. - Done: profile and waiver contracts plus Functions endpoints are implemented with fail-closed `[RequirePolicy]` metadata and endpoint metadata tests. - Done when: profile, guest, waiver, and merge-link scenarios pass.

## M5 - Payments (deferred)

M5 is intentionally deferred while M6 Seasons, venues, and sessions are implemented. Stripe remains the future source of truth for payment state; do not add temporary database-authoritative payment logic.
- [ ] **M5.1** `IPaymentGateway` + Stripe adapter; `Membership`, `PaymentLedger`, `StripeCustomerReference`. â€” Stories: PAY-1,3,4.
- [ ] **M5.2** `POST /payments/checkout` for monthly membership (server-side session, short-lived URL; no secret keys client-side). â€” Stories: PAY-1.
- [ ] **M5.3** `POST /webhooks/stripe` `[AllowAnonymous]`: signature verify on raw body, unique event-ID insert, atomic ledger/membership update, duplicate=2xx no-op, out-of-order guard, failure handling. â€” Stories: PAY-2,6, INV-1.
- [ ] **M5.4** Eligibility projection consumed by RSVP. â€” Done when: concurrent/duplicate/out-of-order webhook tests pass (Â§17).

## M6 â€” Seasons, venues & sessions
- [x] **M6.1** `Season`, `Venue` (+`IMapsService` geocode), `RecurrenceRule`, `Session` (capacity, deadline). - Done: scheduling repository ports/EF implementations, maps fallback provider, and scheduling models are wired. - Stories: SES-1,2,5.
- [x] **M6.2** `Features/Sessions` CRUD + validation (capacity>0, deadline<start). - Done: create/list seasons, venues, sessions plus cancel-session use cases validate UTC, capacity, check-in window, and RSVP deadline. - Stories: SES-4,5.
- [x] **M6.3** Timer trigger for recurring sessions with deterministic occurrence key + unique constraint. - Done: idempotent occurrence creation handler builds deterministic occurrence keys and returns existing sessions on replay; a timer trigger can call this handler later. - Stories: SES-3.
- [x] **M6.4** Endpoints/contracts. - Done: seasons, venues, sessions, recurrence rules, occurrence creation, and cancellation endpoints/contracts are implemented with policy metadata tests. Cancellation notification queuing remains deferred to M10 outbox dispatch. - Done when: replayed occurrence idempotency test passes.
- [x] **M6.5** Admin create/publish session use case with required-field validation, venue-local date/time display, UTC storage, audit fields, and idempotent publish. - Done: backend stores UTC timestamps, validates required scheduling fields, publishes created sessions, and supports idempotent occurrence publish by deterministic key. Venue-local display remains a client formatting concern. - Stories: ADMIN-4, SES-1, SES-2.

## M7 - RSVP & waitlist
- [x] **M7.1** `RsvpResponse`, `WaitlistEntry`, `CheckIn`; unique active-RSVP constraint + `rowversion`. - Done: schema already has RSVP/check-in/waitlist tables, active uniqueness, rowversion, and a status check that keeps waitlisted state out of `RsvpResponses`; repository registration is covered. - Stories: RSVP-1,3, CHK-1.
- [x] **M7.2** `SubmitRsvp`: waiver + payment eligibility gate, then serializable capacity transaction with bounded retry -> 409; idempotency key on the endpoint. - Done: Application checks current waiver plus a payment eligibility seam, the default provider is explicitly deferred until M5, player mutation endpoints require persisted `Idempotency-Key` replay, and Infrastructure uses serializable transactions with bounded retry-to-409 for SQL concurrency conflicts. - Stories: RSVP-1,2, INV-2,3.
- [x] **M7.3** `PromoteWaitlist` on cancel; skip-ineligible; `PlayerWaitlistPromoted` via outbox. - Done: cancellation promotes the next eligible active waitlist entry atomically, skips ineligible entries by expiring them, and persists a `PlayerWaitlistPromoted` outbox message in the same transaction; notification dispatch remains part of M10. - Stories: RSVP-4,5.
- [x] **M7.4** Deadline lock + admin override (audited). - Done: player RSVP/cancel flows reject after the RSVP deadline; admin override bypasses player eligibility and writes an `AdminOverride` audit row using `IClock`. - Stories: RSVP-6,7.
- [x] **M7.5** Check-in + no-show. - Done: GameAdmin endpoints and repository methods record check-ins/no-shows without mutating RSVP intent, enforce the stored check-in window through Application using server UTC, and write late check-in overrides as audited `AdminOverride` rows. - Stories: CHK-1,2, GDAY-1.
- [x] **M7.6** Session-specific guest/drop-in checkout and verified eligibility projection. - Done: authenticated players can request a session-specific drop-in checkout through the payment gateway seam, RSVP eligibility reads active membership or verified session drop-in ledger projection, and checkout initiation does not create database-authoritative payment state. Stripe webhooks remain the source of truth for settled payment state. - Stories: PAY-5.

## M8 â€” Teams, matches & stats
- [x] **M8.1** `Match`, `MatchTeam`, `TeamAssignment` (per-match), `MatchResult`; admin captain assignment for 2/3/4 teams; session-scoped captain draft permissions. - Done: backend create-match use case persists match teams, captains, team assignments, participant rows, and result rows at match grain with 2-4 team validation. - Stories: TEAM-1,2,3, TEAM-4, INV-9.
- [x] **M8.2** `PlayerMatchStats` (row per participant incl. guests); `MatchEvent` (goal/assist/own-goal/cards, <=1 assist/goal); captain approval queue. - Done: participant rows and raw match event replacement are implemented, assists are nullable players on goal rows, `PlayerMatchStats` remains participation-only, and match events now carry pending/approved/rejected captain review metadata with scoped captain/GameAdmin review. - Stories: STAT-1,2, STAT-9, INV-7.
- [x] **M8.3** `PlayerRatingVote` (0-10, unique `(MatchId,Voter,Rated)`, no self-vote), `PlayerLike`, `MatchAward`. - Done: peer feedback use case records ratings, likes, and the single explicit MVP award, rejects self vote/like/MVP, and preserves `MatchAward` as the MVP authority. - Stories: STAT-3,4,5, INV-8.
- [x] **M8.4** Lock + `StatCorrection` audit and conflict-to-review resolution for captain approvals/results. - Done: lock endpoint and `StatCorrection` audit command exist; conflicting captain event reviews or changed result submissions move the match to `NeedsReview`, and GameAdmin review resolution requires an audited correction note before returning the match to completed state. - Stories: STAT-6, STAT-9.
- [x] **M8.5** Endpoints/contracts. - Done: payment eligibility/checkout and stats match/events/results/feedback/lock/correction/profile-reassign contracts plus Function endpoints are implemented with policy metadata tests. Aggregation remains M9 by design.
- [x] **M8.6** Complete `ProfileMerge` stat reassignment with an audit trail and no duplication. - Done: stats reassignment handles match assignments, participation, events including submitter/reviewer metadata, ratings, likes, MVP awards, and corrections with duplicate-safe soft deletion, and writes a dedicated profile stat reassignment audit record. - Stories: PROF-4.

## M9 - Leaderboards & queries
- [x] **M9.0** Approve the implementation plan in [`m9-leaderboards-queries.md`](m9-leaderboards-queries.md): query seams, eligible raw facts, sort/tie-break rules, pagination, API shape, and test matrix. - Stories: LEAD-1,2,3, STAT-9, INV-6,7, NFR-Performance.
- [x] **M9.1** `Features/Stats` query handlers: season + career projections from raw rows (`Match -> Session -> Season`), derived-on-read; profile recent form and rotation W/D/L counters from `TeamAssignment` + `MatchResult`, with `wins + draws + losses <= teamCount - 1` preserved from write validation. - Stories: LEAD-1,2, STAT-9, INV-6,7.
- [x] **M9.2** Tie-breakers (Goals -> fewer appearances -> Assists), stable final ordering, and pagination on all stat queries. - Stories: LEAD-3, NFR-Performance.
- [x] **M9.3** Add Function endpoints/contracts for leaderboard pages, player stats, and recent form with fail-closed endpoint metadata. - Stories: LEAD-1,2,3, STAT-9, INV-11.
- [ ] **M9.4** (Optional) Azure Table Storage projection only if a measured read/scale need appears.

## M10 - Notifications & reliability
- [ ] **M10.1** Outbox dispatcher (idempotent, post-commit publish, delivery marking, dead-letter). - Stories: NOTIF-1, NFR-Reliability.
- [ ] **M10.2** `IEmailService`/SendGrid; `ISmsService`/Twilio (gated by M0.4 decision). - Stories: NOTIF-1,2.
- [ ] **M10.3** RSVP/dues reminder timers (single reminder per cycle). - Stories: NOTIF-3.

## M11 â€” Client integration

> **Per-story spec (pilot):** Welcome Back task slices are mirrored under
> [`stories/AUTH-7-welcome-back-screen/tasks.md`](stories/AUTH-7-welcome-back-screen/tasks.md)
> (plus AUTH-8/AUTH-9).

- [x] **M11.0** Reusable MAUI UI foundation: brand color/type/spacing tokens, shared styles,
  eleven MVVM-friendly controls, Shell theming, and a UI Library showcase. The
  `client-ui.md` specification is complete; future product-screen adoption is tracked separately.
  â€” Spec: `client-ui.md`.
- [x] **M11.0a** Add licensed Font Awesome Free Solid and Brands font resources, register
  `FontAwesomeSolid`/`FontAwesomeBrands`, and add a typed glyph catalog. Replace emoji/text
  pictograms used by the Welcome Back screen with Font Awesome glyphs and semantic descriptions.
  â€” Stories: INV-13, AUTH-7 Â· Projects: MAUI client Â· Depends on: M11.0.
- [x] **M11.0c** Extend the reusable UI library for the first product-screen wave: add
  `LeadingContent` to `BrandHeader` and `PlayerRow`; add shared `IconButton`, `IconToggleButton`,
  `MetadataChip`, and `RatingSlider` styles; update the UI Library showcase and accessibility tests.
  â€” Stories: SES-6, PROF-5, LEAD-4, STAT-8 Â· Projects: MAUI client Â· Depends on: M11.0, M11.0a.
- [x] **M11.0b** Add seed-data providers in `SouthBaySoccer/SeedData/` implementing the client service
  interfaces (auth, sessions, roster, stats, leaderboard, profile) with deterministic fixtures matching
  every first-wave wireframe operation; keep immutable baseline fixtures plus resettable,
  application-scoped demo state for RSVP/stats/rating commands. Register Seed by configuration,
  fail fast for unavailable Api or Release+Seed, and let M11.1 complete the Api branch. Seeds are
  Release-guarded and carry no real personal data. â€” Stories: UI-first phase (design.md Â§12) Â· Projects:
  MAUI client Â· Depends on: M11.0.
- [x] **M11.1** `Contracts`-based typed API services + `HttpClient` pipeline (`CorrelationIdHandler` -> `AuthenticationHandler` -> `ApiExceptionHandler`); secure token storage. - Done: API mode registers `HttpClientFactory`, correlation IDs, bearer token attachment, single-flight refresh-token rotation, safe API exceptions, and an API-backed profile client. - Stories: AUTH-4, NFR-Security.
- [x] **M11.2** Replace one MAUI sample repository flow with a typed API client (proves the path). - Done: API mode resolves `IProfileClient` to `ApiProfileClient`, loading `profiles/me` through the shared authenticated pipeline while Seed mode keeps `SeedProfileClient`. - Stories: PROF-1.
- [x] **M11.3a** Implement `WelcomeBackPage` and `WelcomeBackPageModel` directly from the first
  `signin` screen in `documentation/mobile-wireframes.html`: branded header, welcome copy, phone
  input, phone sign-in action, security notice, Pickup Pal bot card, divider, signup action, and caption.
  Use only shared brand resources and Font Awesome glyphs; no emoji or page-local hex values.
  â€” Stories: AUTH-7, INV-13 Â· Projects: MAUI client Â· Depends on: M11.0a.
- [x] **M11.3b** Add typed Pickup Pal configuration, external launcher abstractions, international
  phone validation, busy/error/offline states, and commands for opening the bot and signup page.
  External-return alone must not authenticate the user.
  â€” Stories: AUTH-8, AUTH-9 Â· Projects: MAUI client Â· Depends on: M11.1, M11.3a.
- [x] **M11.3c** Implement Pickup Pal phone sign-in: call the anonymous Function endpoint, prevent duplicate submission, sync the Pickup Pal user locally, issue/store SouthBaySoccer tokens, and replace the auth route with the Sessions Shell only after success.
  - Stories: AUTH-8, AUTH-3, AUTH-4 - Projects: Contracts, Functions, Application, Infrastructure, MAUI client
  - Depends on: M3.4, M11.1, M11.3b.
- [~] **M11.3d** Add `Client.Tests` for Welcome Back startup routing, validation, single-submit,
  external launch failures, phone sign-in completion, secure token storage interaction, semantic icon
  descriptions, large text, and narrow-screen scrolling. Verify against the first wireframe and
  build `net10.0-windows10.0.19041.0`.
  â€” Stories: AUTH-7, AUTH-8, AUTH-9, INV-13 Â· Depends on: M11.3c.
- [x] **M11.3e** Implement the first-wave product screens using the per-story slices in
  `stories/`: SES-6 â†’ RSVP-8, STAT-7 â†’ STAT-8, and PROF-5/LEAD-4 in parallel after M11.0b.
  Integrate shared Shell routes after the owning pages exist.
  â€” Stories: PROF-5, SES-6, RSVP-8, LEAD-4, STAT-7, STAT-8 Â· Depends on: M11.0b, M11.0c.
- [ ] **M11.4** Offline live-stats with idempotency-keyed queue + sync. â€” Stories: ADMIN-2.
- [ ] **M11.5** Move `SouthBaySoccer/` â†’ `src/SouthBaySoccer.Client/` as a dedicated change (no feature work mixed in). â€” architecture Â§5/Â§18.
- [ ] **M11.6** Admin create-session screen against seed state, publishing into the Sessions feed for RSVP. - Stories: ADMIN-4.

## M12 â€” Hardening & deploy
- [ ] **M12.1** Application Insights traces/metrics; correlation across client/Functions/providers. â€” NFR-Observability.
- [ ] **M12.2** Key Vault + managed identity for secrets and SQL; least-privilege identities. â€” architecture Â§8.
- [ ] **M12.3** Rate limiting at API Management/Front Door; deployment topology (Flex Consumption for .NET 10 Linux). â€” architecture Â§16.
- [ ] **M12.4** Controlled migration deployment step; synthetic/health checks; dead-letter alerting.
- [ ] **M12.5** Full Â§17 high-risk suite green before release.

---

## Dependency summary

```
M0 â†’ M1 â†’ M2 â†’ M3 â†’ M4 â†’ M5 â”
                     M4 â†’ M6 â†’ M7 (needs M5 eligibility/payment infrastructure)
                                M7 â†’ M8 â†’ M9
                     M4 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â†’ M8.6 (profile stat merge)
                     M1 â†’ M10
M3..M9 â†’ M11 â†’ M12
```

## Definition of done (per milestone)

A milestone is done when its tasks are checked, all referenced story scenarios have passing tests,
affected backend projects build, relevant test projects pass, affected client TFMs build, no
secrets/PII are committed, and the change set did not mix sample removal with new feature behavior.



