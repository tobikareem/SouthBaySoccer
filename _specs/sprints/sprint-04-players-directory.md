# Sprint 04 - Players Tab Database Directory

**Phase:** Players screen API and database wiring  
**Length:** 1 week  
**Target TFMs:** `net10.0-windows10.0.19041.0`, `net10.0-android`  
**Board:** [`sprint-04-board.md`](sprint-04-board.md)

## Sprint goal

> Make the MAUI Players tab data-driven from the backend database, reading actual players from
> `AspNetUsers` and `PlayerProfiles`, while preserving the existing mobile wireframe design.

Sprint 03 remains parked for the broader API integration backlog. Sprint 04 narrows the next
implementation slice to one user-visible screen: the Players tab. The screen already has a
wireframe-matched XAML layout, `PlayersPageModel`, and `IPlayersClient` abstraction. The missing work
is the backend directory read path and the MAUI API-mode client registration that replaces seed data
with real database rows.

## Design baseline

`documentation/mobile-wireframes.html` remains the baseline for the Players tab. This sprint must not
redesign the screen, change its Shell placement, or introduce page-local styles. Any visual mismatch
is handled by updating the wireframe and shared MAUI tokens/styles first, then the page.

Implementation rule: `PlayersPageModel` continues to depend on `IPlayersClient` and
`IPlayersNavigator`. It must not take a raw `HttpClient`, EF context, or backend domain dependency.

## Current state

- `SouthBaySoccer/Pages/PlayersPage.xaml` already matches the tab structure: header, search, count
  badge, empty/error/offline states, and player rows.
- `SouthBaySoccer/PageModels/PlayersPageModel.cs` already calls
  `IPlayersClient.GetDirectoryAsync()` and filters rows locally by display name, position, and row
  subtitle.
- `src/SouthBaySoccer.Contracts/Players/PlayerDirectoryDtos.cs` already defines
  `PlayerDirectoryDto` and `PlayerDirectoryEntryDto`.
- `src/SouthBaySoccer.Contracts/Players/PlayerSummaryDto.cs` already defines the row summary shape:
  id, display name, initials, position, guest flag, and linked identity id.
- API mode currently still falls back to `SeedPlayersClient`; there is no `ApiPlayersClient`
  registered for `IPlayersClient`.
- The backend has `PlayerProfiles` and identity infrastructure, but `IPlayerProfileRepository` does
  not yet expose a directory query.

## Sprint backlog

| # | Item | Story | Pts | Depends on | Notes |
|---|------|-------|----:|------------|-------|
| 1 | Players contract and data-shape inventory | `PLAYERS-0` | 2 | populated DB | Verify actual table names, profile/identity links, nullable fields, sort order, and whether guest profiles appear in the directory. |
| 2 | Backend players directory query and endpoint | `PLAYERS-1` | 5 | `PLAYERS-0` | Add an Application query and Function endpoint returning `PlayerDirectoryDto` from `PlayerProfiles` plus linked identity data. |
| 3 | MAUI `ApiPlayersClient` and API-mode registration | `PLAYERS-2` | 3 | `PLAYERS-1`, M11.1 | Register `IPlayersClient` to the API provider in API mode; keep Seed mode deterministic. |
| 4 | Players tab API-mode behavior tests | `PLAYERS-3` | 3 | `PLAYERS-2` | Cover loading, refresh, search, empty/error/offline states, route mapping, cancellation, and ProblemDetails handling. |
| 5 | Player profile navigation follow-through | `PLAYERS-4` | 3 | `PLAYERS-1` | Ensure tapping a real player can load a profile/detail path instead of `ApiProfileClient.GetProfileAsync()` returning null. |
| 6 | Local database smoke and runbook | `PLAYERS-5` | 3 | `PLAYERS-2` | Run Functions against the local database with populated `AspNetUsers`/`PlayerProfiles`; smoke the MAUI Players tab in API mode. |
| 7 | Sprint closeout and spec reconciliation | `PLAYERS-6` | 2 | all sprint work | Update board evidence, story notes, and any stale task references after verification. |

**Committed:** 21 pts.

## API contract target

Initial target endpoint:

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `players/directory` | Return the screen-ready player directory for the Players tab. |

The endpoint should return `PlayerDirectoryDto` and avoid exposing phone numbers, email addresses,
payment identifiers, waiver details, or other private profile fields. The directory should include
active, non-deleted `PlayerProfiles`; linked identity data from `AspNetUsers` may be used to enrich
identity id and display data, but private identity fields stay server-side.

If implementation shows an existing route is already intended for this projection, prefer using that
route and update this sprint spec and board before coding against it.

## Definition of Done

- API mode resolves `IPlayersClient` to `ApiPlayersClient`; Seed mode still resolves
  `SeedPlayersClient`.
- The Players tab loads actual database players from `PlayerProfiles` and linked `AspNetUsers`
  data, not seed rows.
- The existing wireframe-shaped XAML and reusable controls remain intact.
- Backend read logic filters soft-deleted profiles and uses UTC/audit conventions where applicable.
- The endpoint returns no private profile, payment, token, or contact data.
- Tests cover repository/query mapping, Function response shape, API client route/error behavior, and
  `PlayersPageModel` state transitions.
- Local API-mode smoke confirms the populated database appears in the MAUI Players tab.
- Windows and Android builds pass with no new warnings before sprint closeout.

## Out of scope

- Redesigning the Players tab.
- Building player edit/admin management workflows.
- Adding payment status badges or waiver gates to the directory.
- Replacing the shared MAUI control library.
- Completing the rest of Sprint 03 API integration.
