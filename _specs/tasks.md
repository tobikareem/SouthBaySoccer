# SouthBaySoccer — Tasks & Sequential Roadmap

Ordered, dependency-aware implementation plan. Tasks trace to stories in
[requirements.md](requirements.md) and components in [design.md](design.md), and follow the
incremental adoption order in [`documentation/architecture.md`](../documentation/architecture.md) §18.

**Conventions**
- Task ID `M<milestone>.<n>`. Each task lists **Stories**, **Projects**, **Depends on**, and **Done when**.
- "Done when" always includes: affected backend projects build, relevant test projects pass, the
  affected MAUI target builds when client code changes, new behavior is covered by tests, no new
  warnings, and no secrets are committed.
- Work one milestone at a time; each milestone leaves the solution buildable (architecture §18). Don't mix sample removal with new feature behavior.
- Status: `[ ]` todo · `[~]` in progress · `[x]` done.

---

## Delivery strategy — UI-first (current)

The current focus is the **MAUI/XAML client**. Backend milestones **M1–M10** (Domain, Application,
Infrastructure, Functions, Azure SQL) are **deferred**. Client stories that need data use **seed-data
providers** (`M11.0b`) behind client service interfaces, swapped for the typed API client (`M11.1`)
later with no screen change. During this phase, client tasks are **not** blocked on `M3`; backend
wiring is completed in the backend phase. See `design.md` §12.

## Sequential roadmap (high level)

1. **M0 Foundation** — shared kernel, `BaseEntity`, cross-cutting abstractions, CI.
2. **M1 Persistence & Identity core** — EF Core `DbContext`, audit/soft-delete, ASP.NET Identity stores, migrations.
3. **M2 Functions pipeline** — middleware (correlation/exception/authn/authz), fail-closed classification, ProblemDetails.
4. **M3 Authentication** — register, confirm, sign-in, refresh rotation, reset (AUTH).
5. **M4 Player profiles & waivers** — profiles, guests, emergency contact, waiver accept + gating (PROF, WAIV).
6. **M5 Payments** — Stripe checkout + idempotent webhooks + membership/ledger (PAY).
7. **M6 Seasons, venues & sessions** — seasons, venues, recurring sessions timer (SES).
8. **M7 RSVP & waitlist** — eligibility-gated RSVP, transactional capacity, waitlist promotion (RSVP, CHK).
9. **M8 Teams, matches & stats** — matches, per-match teams, events, ratings, likes, MVP, corrections (TEAM, STAT).
10. **M9 Leaderboards & queries** — season/career read projections, tie-breakers (LEAD).
11. **M10 Notifications & reliability** — outbox dispatcher, SendGrid/Twilio, reminders (NOTIF).
12. **M11 Client integration** — typed API clients, token handling, replace one sample flow, offline live-stats (ADMIN-2), then client move to `src/SouthBaySoccer.Client/`.
13. **M12 Hardening & deploy** — observability, rate limiting, Key Vault/managed identity, deployment topology, controlled migrations.

---

## M0 — Foundation
- [~] **M0.1** Define `BaseEntity`, common value objects, `IClock`, `ICurrentUser`, result/error types. — Current: `BaseEntity`, `IClock`, `ICurrentUser`, and transport `ApiError` exist; common value objects, an Application result type, and real `Domain.Tests` remain. — Stories: INV-10 · Projects: Domain, Application.
- [~] **M0.2** Add domain enumerations (roles, event types, payment/membership status, RSVP intent status, attendance outcome). — Current: role/payment/RSVP intent enums exist; event types and attendance outcome remain. — Projects: Domain.
- [ ] **M0.3** Establish CI: build backend projects, run all layer-specific test projects, build the
  Windows MAUI TFM, run analyzers and `dotnet format`. — Done when: pipeline green on the skeleton.
- [~] **M0.4** Confirm decisions in design.md §10. — Current: membership is resolved as monthly membership plus session-specific guest/drop-in eligibility; SMS timing, balancing, and clean-sheet threshold remain. — Output: decisions recorded in design.md and durable conventions mirrored in `.ai/memory/`.

## M1 — Persistence & Identity core
- [~] **M1.1** `SouthBaySoccerDbContext` + `IUnitOfWork`; audit-field save interceptor (`ICurrentUser`+`IClock`); global `IsDeleted==false` filter. — Current: empty `DbContext`, `UnitOfWork`, and Azure SQL DI registration exist; mappings, interceptor, and filter remain. — Projects: Infrastructure · Depends on: M0.1.
- [ ] **M1.2** `ApplicationIdentityUser : IdentityUser<Guid>`, `AddIdentityCore`, role/token providers, EF Identity stores; shared Data Protection keys. — Stories: AUTH-* · Projects: Infrastructure.
- [ ] **M1.3** Refresh-token records (hashed, revocable, rotation/reuse, family revoke) + `ProcessedWebhookEvent`, outbox tables — exempt from soft-delete with retention. — Stories: AUTH-4, PAY-2, NOTIF-1.
- [ ] **M1.4** First migration; document controlled (non-cold-start) migration runner. — Done when: `Infrastructure.Tests` run against SQL-compatible infra.

## M2 — Functions pipeline
- [ ] **M2.1** Middleware order: correlation → exception → authentication → authorization → execute. — Projects: Functions · Depends on: M1.2.
- [ ] **M2.2** `EndpointPolicyResolver`, `[RequirePolicy]`/`[AllowAnonymous]`, fail-closed rejection of unclassified/conflicting endpoints. — Stories: INV-11, NFR-AuthZ.
- [ ] **M2.3** RFC 7807 `ProblemDetails` exception mapping (400/401/403/404/409/429/500); correlation-ID logging; no sensitive data in responses/logs.
- [ ] **M2.4** `Functions.Tests`: classification, authz, ProblemDetails, correlation. — Done when: high-risk authz scenarios pass.

## M3 — Authentication
- [ ] **M3.1** `ITokenService` (JWT issue/validate, signing-key rotation via Key Vault with overlap). — Stories: AUTH-3.
- [ ] **M3.2** `Features/Authentication`: register, confirm-email, sign-in, password reset (FluentValidation). — Stories: AUTH-1,2,3,5,6.
- [ ] **M3.3** Refresh-token exchange with atomic rotation, family revocation, and reuse detection.
  Client-side single-flight refresh is implemented in M11.1. — Stories: AUTH-4.
- [ ] **M3.4** HTTP functions + contracts for the above (`[AllowAnonymous]` where intended). — Done when: AUTH scenarios pass in `Application.Tests`/`Functions.Tests`.

## M4 — Player profiles & waivers
- [ ] **M4.1** `PlayerProfile` (+`IsGuest`), `EmergencyContact`; configs + repository. — Stories: PROF-1,2,3.
- [ ] **M4.2** `Features/Players`: get/update me, create guest, and create an auditable
  `ProfileMerge` link/retirement workflow. Defer stat reassignment until the match-stat model exists.
  — Stories: PROF-1,3,4.
- [ ] **M4.3** `WaiverDocument` (versioned) + `WaiverAcceptance`; accept use case; eligibility helper. — Stories: WAIV-1,2,3.
- [ ] **M4.4** Endpoints + contracts. — Done when: profile, guest, waiver, and merge-link scenarios pass.

## M5 — Payments
- [ ] **M5.1** `IPaymentGateway` + Stripe adapter; `Membership`, `PaymentLedger`, `StripeCustomerReference`. — Stories: PAY-1,3,4.
- [ ] **M5.2** `POST /payments/checkout` for monthly membership (server-side session, short-lived URL; no secret keys client-side). — Stories: PAY-1.
- [ ] **M5.3** `POST /webhooks/stripe` `[AllowAnonymous]`: signature verify on raw body, unique event-ID insert, atomic ledger/membership update, duplicate=2xx no-op, out-of-order guard, failure handling. — Stories: PAY-2,6, INV-1.
- [ ] **M5.4** Eligibility projection consumed by RSVP. — Done when: concurrent/duplicate/out-of-order webhook tests pass (§17).

## M6 — Seasons, venues & sessions
- [ ] **M6.1** `Season`, `Venue` (+`IMapsService` geocode), `RecurrenceRule`, `Session` (capacity, deadline). — Stories: SES-1,2,5.
- [ ] **M6.2** `Features/Sessions` CRUD + validation (capacity>0, deadline<start). — Stories: SES-4,5.
- [ ] **M6.3** Timer trigger for recurring sessions with deterministic occurrence key + unique constraint. — Stories: SES-3.
- [ ] **M6.4** Endpoints/contracts; cancellation queues notification. — Done when: replayed-timer idempotency test passes.
- [ ] **M6.5** Admin create/publish session use case with required-field validation, venue-local date/time display, UTC storage, audit fields, and idempotent publish. - Stories: ADMIN-4, SES-1, SES-2.

## M7 — RSVP & waitlist
- [ ] **M7.1** `RsvpResponse`, `WaitlistEntry`, `CheckIn`; unique active-RSVP constraint + `rowversion`. — Stories: RSVP-1,3, CHK-1.
- [ ] **M7.2** `SubmitRsvp`: waiver + payment eligibility gate, then serializable capacity transaction with bounded retry → 409; idempotency key on the endpoint. — Stories: RSVP-1,2, INV-2,3.
- [ ] **M7.3** `PromoteWaitlist` on cancel; skip-ineligible; `PlayerWaitlistPromoted` via outbox. — Stories: RSVP-4,5.
- [ ] **M7.4** Deadline lock + admin override (audited). — Stories: RSVP-6,7.
- [ ] **M7.5** Check-in + no-show. - Stories: CHK-1,2, GDAY-1. - Done when: final-slot concurrency + promotion tests pass (section 17), and the 7:30 PM-7:45 PM venue-local check-in window plus late override audit are covered.
- [ ] **M7.6** Session-specific guest/drop-in checkout and verified eligibility projection. — Depends on: M5 payment infrastructure, M6 Session. — Stories: PAY-5.

## M8 — Teams, matches & stats
- [ ] **M8.1** `Match`, `MatchTeam`, `TeamAssignment` (per-match), `MatchResult`; admin captain assignment for 2/3/4 teams; session-scoped captain draft permissions. - Stories: TEAM-1,2,3, TEAM-4, INV-9.
- [ ] **M8.2** `PlayerMatchStats` (row per participant incl. guests); `MatchEvent` (goal/assist/own-goal/cards, <=1 assist/goal); captain approval queue. - Stories: STAT-1,2, STAT-9, INV-7.
- [ ] **M8.3** `PlayerRatingVote` (0–10, unique `(MatchId,Voter,Rated)`, no self-vote), `PlayerLike`, `MatchAward`. — Stories: STAT-3,4,5, INV-8.
- [ ] **M8.4** Lock + `StatCorrection` audit and conflict-to-review resolution for captain approvals/results. - Stories: STAT-6, STAT-9.
- [ ] **M8.5** Endpoints/contracts. — Done when: rating-integrity + aggregation tests pass (§17).
- [ ] **M8.6** Complete `ProfileMerge` stat reassignment with an audit trail and no duplication. — Depends on: M4.2, M8.2. — Stories: PROF-4.

## M9 — Leaderboards & queries
- [ ] **M9.1** `Features/Stats` query handlers: season + career projections from raw rows (`Match -> Session -> Season`), derived-on-read; profile recent form and rotation W/D/L counters from `TeamAssignment` + `MatchResult`, with `wins + draws + losses <= teamCount - 1` validation. - Stories: LEAD-1,2, STAT-9, INV-6,7.
- [ ] **M9.2** Tie-breakers (Goals → fewer appearances → Assists); pagination on all stat queries. — Stories: LEAD-3, NFR-Performance.
- [ ] **M9.3** (Optional) Azure Table Storage projection only if a measured read/scale need appears.

## M10 — Notifications & reliability
- [ ] **M10.1** Outbox dispatcher (idempotent, post-commit publish, delivery marking, dead-letter). — Stories: NOTIF-1, NFR-Reliability.
- [ ] **M10.2** `IEmailService`/SendGrid; `ISmsService`/Twilio (gated by M0.4 decision). — Stories: NOTIF-1,2.
- [ ] **M10.3** RSVP/dues reminder timers (single reminder per cycle). — Stories: NOTIF-3.

## M11 — Client integration

> **Per-story spec (pilot):** Welcome Back task slices are mirrored under
> [`stories/AUTH-7-welcome-back-screen/tasks.md`](stories/AUTH-7-welcome-back-screen/tasks.md)
> (plus AUTH-8/AUTH-9).

- [x] **M11.0** Reusable MAUI UI foundation: brand color/type/spacing tokens, shared styles,
  eleven MVVM-friendly controls, Shell theming, and a UI Library showcase. The
  `client-ui.md` specification is complete; future product-screen adoption is tracked separately.
  — Spec: `client-ui.md`.
- [x] **M11.0a** Add licensed Font Awesome Free Solid and Brands font resources, register
  `FontAwesomeSolid`/`FontAwesomeBrands`, and add a typed glyph catalog. Replace emoji/text
  pictograms used by the Welcome Back screen with Font Awesome glyphs and semantic descriptions.
  — Stories: INV-13, AUTH-7 · Projects: MAUI client · Depends on: M11.0.
- [x] **M11.0c** Extend the reusable UI library for the first product-screen wave: add
  `LeadingContent` to `BrandHeader` and `PlayerRow`; add shared `IconButton`, `IconToggleButton`,
  `MetadataChip`, and `RatingSlider` styles; update the UI Library showcase and accessibility tests.
  — Stories: SES-6, PROF-5, LEAD-4, STAT-8 · Projects: MAUI client · Depends on: M11.0, M11.0a.
- [x] **M11.0b** Add seed-data providers in `SouthBaySoccer/SeedData/` implementing the client service
  interfaces (auth, sessions, roster, stats, leaderboard, profile) with deterministic fixtures matching
  every first-wave wireframe operation; keep immutable baseline fixtures plus resettable,
  application-scoped demo state for RSVP/stats/rating commands. Register Seed by configuration,
  fail fast for unavailable Api or Release+Seed, and let M11.1 complete the Api branch. Seeds are
  Release-guarded and carry no real personal data. — Stories: UI-first phase (design.md §12) · Projects:
  MAUI client · Depends on: M11.0.
- [~] **M11.1** `Contracts`-based typed API services + `HttpClient` pipeline (`CorrelationIdHandler`→`AuthenticationHandler`→`ApiExceptionHandler`); secure token storage. — Current: typed authentication client and secure token storage are implemented for AUTH-7/8; the shared correlation/authentication/exception handler pipeline remains. — Stories: AUTH-4, NFR-Security.
- [ ] **M11.2** Replace one MAUI sample repository flow with a typed API client (proves the path). — Stories: PROF-1.
- [x] **M11.3a** Implement `WelcomeBackPage` and `WelcomeBackPageModel` directly from the first
  `signin` screen in `documentation/mobile-wireframes.html`: branded header, welcome copy, phone
  input, WhatsApp action, security notice, Pickup Pal bot card, divider, signup action, and caption.
  Use only shared brand resources and Font Awesome glyphs; no emoji or page-local hex values.
  — Stories: AUTH-7, INV-13 · Projects: MAUI client · Depends on: M11.0a.
- [x] **M11.3b** Add typed Pickup Pal configuration, external launcher abstractions, international
  phone validation, busy/error/offline states, and commands for opening the bot and signup page.
  External-return alone must not authenticate the user.
  — Stories: AUTH-8, AUTH-9 · Projects: MAUI client · Depends on: M11.1, M11.3a.
- [~] **M11.3c** Implement the WhatsApp one-time challenge client flow and approved deep-link
  callback: request challenge, prevent duplicate submission, verify/exchange the callback, store
  tokens securely, and replace the auth route with the Sessions Shell only after success.
  — Stories: AUTH-8, AUTH-3, AUTH-4 · Projects: Contracts, Functions, Application, MAUI client
  · Depends on: M3.4, M11.1, M11.3b.
- [~] **M11.3d** Add `Client.Tests` for Welcome Back startup routing, validation, single-submit,
  external launch failures, deep-link completion, secure token storage interaction, semantic icon
  descriptions, large text, and narrow-screen scrolling. Verify against the first wireframe and
  build `net10.0-windows10.0.19041.0`.
  — Stories: AUTH-7, AUTH-8, AUTH-9, INV-13 · Depends on: M11.3c.
- [~] **M11.3e** Implement the first-wave product screens using the per-story slices in
  `stories/`: SES-6 → RSVP-8, STAT-7 → STAT-8, and PROF-5/LEAD-4 in parallel after M11.0b.
  Integrate shared Shell routes after the owning pages exist.
  — Stories: PROF-5, SES-6, RSVP-8, LEAD-4, STAT-7, STAT-8 · Depends on: M11.0b, M11.0c.
- [ ] **M11.4** Offline live-stats with idempotency-keyed queue + sync. — Stories: ADMIN-2.
- [ ] **M11.5** Move `SouthBaySoccer/` → `src/SouthBaySoccer.Client/` as a dedicated change (no feature work mixed in). — architecture §5/§18.
- [ ] **M11.6** Admin create-session screen against seed state, publishing into the Sessions feed for RSVP. - Stories: ADMIN-4.

## M12 — Hardening & deploy
- [ ] **M12.1** Application Insights traces/metrics; correlation across client/Functions/providers. — NFR-Observability.
- [ ] **M12.2** Key Vault + managed identity for secrets and SQL; least-privilege identities. — architecture §8.
- [ ] **M12.3** Rate limiting at API Management/Front Door; deployment topology (Flex Consumption for .NET 10 Linux). — architecture §16.
- [ ] **M12.4** Controlled migration deployment step; synthetic/health checks; dead-letter alerting.
- [ ] **M12.5** Full §17 high-risk suite green before release.

---

## Dependency summary

```
M0 → M1 → M2 → M3 → M4 → M5 ┐
                     M4 → M6 → M7 (needs M5 eligibility/payment infrastructure)
                                M7 → M8 → M9
                     M4 ───────────→ M8.6 (profile stat merge)
                     M1 → M10
M3..M9 → M11 → M12
```

## Definition of done (per milestone)

A milestone is done when its tasks are checked, all referenced story scenarios have passing tests,
affected backend projects build, relevant test projects pass, affected client TFMs build, no
secrets/PII are committed, and the change set did not mix sample removal with new feature behavior.
