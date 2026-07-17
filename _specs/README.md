# `_specs/` — Spec-Driven Development

This folder is the executable specification for SouthBaySoccer. It turns the architecture into
build-ready work: **what** we're building (requirements), **how** it maps to the system (design),
**in what order** (tasks), and the **client UI** design system.

## Current delivery strategy - API integration

The first MAUI/XAML screens were built UI-first against seed-data providers. The backend is now
substantially implemented through M9, with M5 Stripe payments, M10 notifications, M12 hardening, and
some client API-mode wiring still open. Current product work should preserve Seed mode for demos and
tests while wiring MAUI service interfaces to the Functions API. See the Sprint 03 and Sprint 04
boards under `sprints/`.

## Documents

| File | Purpose |
|------|---------|
| [`requirements.md`](requirements.md) | Whole-product user stories with **Gherkin** (`Given/When/Then`) acceptance criteria, grouped by epic, each with a stable ID (e.g. `RSVP-3`) and global invariants. |
| [`design.md`](design.md) | Maps requirements onto the architecture: current-state gap analysis, layer/component mapping, domain model, API surface, key flows, authorization, persistence, and test mapping. |
| [`tasks.md`](tasks.md) | Ordered, dependency-aware milestones/tasks (`M0…M12`) tracing back to story IDs, plus the high-level sequential roadmap. |
| [`client-ui.md`](client-ui.md) | MAUI client **reusable UI / design system**: color/typography/spacing tokens, shared styles, and the custom XAML control catalog (maps to milestone M11). |
| [`../documentation/mobile-wireframes.html`](../documentation/mobile-wireframes.html) | Authoritative mobile visual hierarchy, screen composition, interaction states, and navigation reference. |
| [`stories/`](stories/README.md) | **Per-story specs** — each story in its own directory with separate requirements/design/tasks. Pilot: AUTH-7/8/9 (Welcome Back). Cross-cutting rules stay in the overviews above. |

Authority: [`../documentation/architecture.md`](../documentation/architecture.md) is the
authoritative architecture. The spec realizes it; it never re-decides it.

## How to work with this spec

1. **Pick the next milestone** in `tasks.md` (work one at a time; keep the solution buildable).
2. **Read the referenced stories** in `requirements.md` — the Gherkin scenarios are the acceptance tests.
3. **Check `design.md`** and, for client work, open `mobile-wireframes.html` before using
   `client-ui.md` to implement the matching reusable controls and screen composition.
4. **Implement + test**: every scenario becomes a test (`MethodName_StateUnderTest_ExpectedBehavior`); run `dotnet test .\SouthBaySoccer.slnx`.
5. **Check the task box** and capture any durable decision in `.ai/memory`, any gotcha in `.ai/lessons`.

## Gherkin convention

```gherkin
Scenario: <observable outcome>
  Given <starting state / preconditions>
  When  <the action under test>
  Then  <the expected result>
  And   <additional expectations>
```

One scenario per behavior, including the failure/edge cases. The architecture's required high-risk
scenarios (§17) are first-class acceptance tests.

## Status

| Epic | Stories | Milestone(s) | State |
|------|---------|--------------|-------|
| AUTH — Authentication | AUTH-1..6 | M1–M3 | Spec ready |
| PROF — Profiles & guests | PROF-1..4 | M4 | Spec ready |
| WAIV — Waivers | WAIV-1..3 | M4 | Spec ready |
| PAY — Payments | PAY-1..6 | M5 | Spec ready |
| SES — Sessions | SES-1..5 | M6 | Spec ready |
| RSVP — RSVP & waitlist | RSVP-1..7, CHK-1..2 | M7 | Spec ready |
| TEAM/STAT — Matches & stats | TEAM-1..3, STAT-1..6 | M8 | Spec ready |
| LEAD — Leaderboards | LEAD-1..3 | M9 | Spec ready |
| NOTIF — Notifications | NOTIF-1..3 | M10 | Spec ready |
| ADMIN — Admin/live | ADMIN-1..3 | M11 | Spec ready |
| Client reusable UI | design system | M11.0 | Complete; product adoption tracked by separate M11 story tasks |

The original skeleton phase is past. Backend features through M9 are largely present, the MAUI
client has seed-backed product screens plus partial API-mode clients, and the active near-finish
slice is Sprint 04 Players tab API/database wiring. Use `tasks.md` and the sprint boards as the
status source of truth before starting work.

## Open decisions (resolve in M0.4)

Membership model (subscription vs. per-session vs. both) · SMS in v1 or later · team-balancing
algorithm · goalkeeper clean-sheet minutes threshold. See `design.md` §10.

> Product-direction updates still to fold into older overview docs:
> sign-in is direct **phone-number sign-in backed by Pickup Pal lookup**. Users enter a phone
> number; SouthBaySoccer calls Pickup Pal to confirm the number exists, then issues local app tokens.
> WhatsApp challenge/link authentication is deferred. Match goals/assists are **self-submitted then
> captain/admin-confirmed** (a `Pending -> Confirmed` state). See `client-ui.md` and the wireframes.
