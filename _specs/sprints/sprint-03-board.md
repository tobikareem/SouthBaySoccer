# Sprint 03 - MAUI API Integration Board

Living tracker for [`sprint-03-api-integration.md`](sprint-03-api-integration.md). Columns mirror
the implementation tasks needed to move the shipped MAUI app from seed data to the Function App API
while preserving the mobile wireframe.

**Status keys:** `To do` - `In progress` - `In review` - `Done` - `Blocked`.

## Snapshot

| Metric | Pts |
|--------|----:|
| Committed | 37 |
| Done | 3 |
| In progress | 8 |
| In review | 0 |
| To do | 26 |
| Blocked | 0 |

## Sprint commitment

| Card | Story | Pts | Status | Depends on |
|------|-------|----:|--------|------------|
| API inventory and contract gaps | `API-0` | 3 | Done | M11.1 |
| Auth/session API hardening | `AUTH-8`, `AUTH-9` | 3 | To do | M3, M11.1 |
| Sessions/roster/RSVP API clients | `SES-6`, `RSVP-8`, `PAY-5` | 8 | To do | M6, M7, M11.1 |
| Stats/leaderboard API clients | `LEAD-4`, `STAT-7`, `STAT-8`, `STAT-9` | 8 | To do | M8, M9, M11.1 |
| Admin/game-day API clients | `ADMIN-4`, `GDAY-1`, `TEAM-4`, `STAT-9` | 8 | In progress | M6.5, M7.5, M8 |
| API-mode smoke and docs | `M11` | 5 | To do | API clients |
| Spec status reconciliation | `SPEC-3` | 2 | To do | roadmap/code review |

## Board

### To do

| Card | Story | Pts | Tasks | Notes |
|------|-------|----:|-------|-------|
| **Auth/session API hardening** | `AUTH-8`, `AUTH-9` | 3 | phone sign-in `[ ]` / refresh `[ ]` / sign-out `[ ]` / tests `[ ]` | Current sign-in is phone lookup through Pickup Pal; WhatsApp challenge/link auth is deferred. Keep auth endpoints anonymous where intended; protected clients use bearer + refresh pipeline. |
| **Sessions/roster/RSVP API clients** | `SES-6`, `RSVP-8`, `PAY-5` | 8 | dashboard `[ ]` / detail `[ ]` / roster `[ ]` / RSVP `[ ]` / eligibility `[ ]` / tests `[ ]` | Preserve the `home` and `session` wireframes; RSVP remains intent only. |
| **Stats/leaderboard API clients** | `LEAD-4`, `STAT-7`, `STAT-8`, `STAT-9` | 8 | leaderboard `[ ]` / match stats `[ ]` / confirm `[ ]` / feedback `[ ]` / tests `[ ]` | Stats must derive from approved raw match facts; no mutable profile totals. |
| **API-mode smoke and docs** | `M11` | 5 | config `[ ]` / runbook `[ ]` / local smoke `[ ]` / no-secret check `[ ]` | Document local Function App + MAUI API mode setup without committing secrets. |
| **Spec status reconciliation** | `SPEC-3` | 2 | verify `[ ]` / update tasks `[ ]` / notes `[ ]` | Reconcile story task files that lag behind `_specs/tasks.md` roadmap status. |

### In progress

| Card | Story | Pts | Tasks | Notes |
|------|-------|----:|-------|-------|
| **Admin/game-day API clients** | `ADMIN-4`, `GDAY-1`, `TEAM-4`, `STAT-9` | 8 | create/edit/publish `[x]` / check-in `[ ]` / captains `[ ]` / draft `[ ]` / post-game `[ ]` / tests `[~]` | Create Session API vertical slice added backend admin workflow endpoints plus `ApiSessionAdminClient`; remaining game-day workflows still open. |

### In review

_(none)_

### Done

| Card | Story | Pts | Evidence |
|------|-------|----:|----------|
| **API inventory and contract gaps** | `API-0` | 3 | [`sprint-03-api-0-contract-inventory.md`](sprint-03-api-0-contract-inventory.md) compares MAUI interfaces with Function routes and records the Create Session API contract decision. |

### Blocked

_(none)_

## Requirements checklist

- [ ] API mode registers typed providers for all committed MAUI client interfaces.
- [ ] Seed mode remains deterministic and covered by existing tests.
- [ ] Page models remain interface-driven with no raw `HttpClient`.
- [ ] UI stays aligned to `documentation/mobile-wireframes.html`.
- [ ] API-client tests cover routes, payloads, idempotency headers, cancellation tokens, and ProblemDetails mapping.
- [ ] Local API-mode smoke passes without secrets or personal/payment data in source or logs.
- [ ] Windows and Android MAUI builds pass with no new warnings.

## Review notes from sprint creation

- `ApiProfileClient` is currently the only concrete API-mode screen-data client found in the MAUI
  client.
- `ClientServiceCollectionExtensions` is the main registration point to extend for new API clients.
- Function routes already exist for auth, profiles, waivers, sessions, RSVP/check-in, payments,
  stats, and leaderboards, but dedicated game-day read projections may still need endpoint work.
- `_specs/tasks.md` marks several backend slices complete that per-story task files still list as
  open; reconcile after verifying the current implementation and tests.
- API-0 found that Create Session should start by adding backend admin workflow endpoints matching
  `ISessionAdminClient` instead of faking draft/publish behavior inside MAUI.

## How to keep this current

1. As work starts, move a card from **To do** to **In progress** and flip the task fragments from
   `[ ]` to `[~]`.
2. Move to **In review** only after implementation and tests are ready for review.
3. Move to **Done** only when the sprint Definition of Done and the story-specific checks pass.
4. Record blockers here with the exact endpoint, contract, test, or wireframe conflict.

