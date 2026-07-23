# Agent Memory — Index

One line per memory. Skim this at the start of a task; read the full entry when relevant.

- [brand-green-white](brand-green-white.md) — Brand is green & white (Nigerian flag), primary #008751
- [stripe-webhook-source-of-truth](stripe-webhook-source-of-truth.md) — Payment status is driven by verified Stripe webhooks, never the client
- [project-root-and-skills](project-root-and-skills.md) — SouthBaySoccer is the solution root; project skills live in skills/
- [membership-and-drop-in-eligibility](membership-and-drop-in-eligibility.md) — Eligibility supports monthly members and session-specific guests
- [spec-driven-development](spec-driven-development.md) — Specs live in _specs/ (requirements, design, tasks, client-ui); Gherkin
- [client-reusable-ui](client-reusable-ui.md) — MAUI client uses a token-driven reusable UI design system; spec in _specs/client-ui.md
- [ui-first-seed-data](ui-first-seed-data.md) — API integration phase with Seed mode retained for deterministic demos/tests
- [mobile-wireframes-design-source](mobile-wireframes-design-source.md) — mobile-wireframes.html is the authoritative visual and interaction reference for the MAUI client
- [maui-shell-route-ownership](maui-shell-route-ownership.md) — Shell roots belong in AppShell; register only detail routes globally
- [inter-ui-font](inter-ui-font.md) � MAUI product UI uses Inter from Google Fonts
- [m1-audit-soft-delete-rule](m1-audit-soft-delete-rule.md) - M1.1 audit stamping, mutable soft deletes, and immutable hard-delete protection
- [m1-identity-core-registration](m1-identity-core-registration.md) - M1.2 Identity Core, EF stores, token provider, and Data Protection registration
- [m1-operational-records](m1-operational-records.md) - M1.3 refresh-token, webhook, and outbox operational-record persistence rules
- [controlled-migrations](controlled-migrations.md) - EF migrations run only as controlled deployment steps, never Function cold start
- [functions-pipeline-authz](functions-pipeline-authz.md) - Functions pipeline order and fail-closed endpoint access metadata
- [functions-problem-details](functions-problem-details.md) - Functions RFC 7807 status mapping, correlation IDs, and safe error details
- [m3-whatsapp-session-auth](m3-whatsapp-session-auth.md) - M3 is phone-number sign-in via Pickup Pal lookup; WhatsApp challenge auth is deferred
- [m4-profiles-waivers](m4-profiles-waivers.md) - M4 profile anchor, guest profile, emergency contact privacy, and current-waiver eligibility rules
- [m6-scheduling](m6-scheduling.md) - M5 payment deferral, M6 UTC scheduling, recurrence occurrence keys, and cancellation notification boundary
- [m7-rsvp-waitlist](m7-rsvp-waitlist.md) - M7 waitlist source of truth, serializable RSVP writes, and deferred payment eligibility seam
- [m7-check-in-window](m7-check-in-window.md) - M7 check-in window enforcement and late override audit rules
- [m8-teams-stats](m8-teams-stats.md) - M8 match-grain stats authority, MVP, and profile stat reassignment rules
- [m9-leaderboards-queries](m9-leaderboards-queries.md) - M9 leaderboard/profile stats derive from approved raw match facts
- [postman-http-collections](postman-http-collections.md) - SouthBaySoccer Postman workspace collections mirror the repo http folder
- [pickuppal-phone-sign-in](pickuppal-phone-sign-in.md) - Pickup Pal phone lookup is the user source of truth; SouthBaySoccer still issues local tokens
- [maui-api-client-pipeline](maui-api-client-pipeline.md) - MAUI API mode uses HttpClientFactory with correlation, bearer auth, refresh, and API exception handlers
- [sessions-home-profile-greeting](sessions-home-profile-greeting.md) - Sessions home greeting comes from profiles/me plus local device time
- [sprint-03-api-integration](sprint-03-api-integration.md) - Sprint 03 wires the MAUI app to the Functions API while preserving wireframe-matched screens
- [sprint-03-resume-later](sprint-03-resume-later.md) - Sprint 03 API integration is parked and should be resumed later from its board
- [sprint-04-players-directory](sprint-04-players-directory.md) - Sprint 04 makes the Players tab data-driven from AspNetUsers and PlayerProfiles
- [pickuppal-games-import](pickuppal-games-import.md) - Pickup Pal active-games import: source-of-truth sessions, sanitized snapshots, roster union
- [game-day-today-projection](game-day-today-projection.md) - Server-authoritative Pacific game-day selection, self check-in, and audited late overrides
- [game-day-team-workflows](game-day-team-workflows.md) - Server-enforced captain, draft, explicit team-lock, and postgame workflow transitions
- [session-feed-attendance-projection](session-feed-attendance-projection.md) - Shared local + Pickup Pal counts and capacity semantics for Sessions feeds and RSVP writes
