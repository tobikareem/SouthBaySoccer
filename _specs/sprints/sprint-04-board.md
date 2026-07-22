# Sprint 04 - Players Tab Database Directory Board

Living tracker for [`sprint-04-players-directory.md`](sprint-04-players-directory.md). Columns track
the work needed to make the MAUI Players tab read actual player rows from the backend database while
preserving the mobile wireframe.

**Status keys:** `To do` - `In progress` - `In review` - `Done` - `Blocked`.

## Snapshot

| Metric | Pts |
|--------|----:|
| Committed | 21 |
| Done | 16 |
| In progress | 5 |
| In review | 0 |
| To do | 0 |
| Blocked | 0 |

## Sprint commitment

| Card | Story | Pts | Status | Depends on |
|------|-------|----:|--------|------------|
| Players contract and data-shape inventory | `PLAYERS-0` | 2 | Done | populated DB |
| Backend players directory query and endpoint | `PLAYERS-1` | 5 | Done | `PLAYERS-0` |
| MAUI `ApiPlayersClient` and API-mode registration | `PLAYERS-2` | 3 | Done | `PLAYERS-1`, M11.1 |
| Players tab API-mode behavior tests | `PLAYERS-3` | 3 | Done | `PLAYERS-2` |
| Player profile navigation follow-through | `PLAYERS-4` | 3 | Done | `PLAYERS-1` |
| Local database smoke and runbook | `PLAYERS-5` | 3 | In progress | `PLAYERS-2` |
| Sprint closeout and spec reconciliation | `PLAYERS-6` | 2 | In progress | all sprint work |

## Board

### To do

_(none)_

### In progress

| Card | Story | Pts | Tasks | Notes |
|------|-------|----:|-------|-------|
| **Local database smoke and runbook** | `PLAYERS-5` | 3 | API `[x]` / native MAUI `[ ]` | Authenticated API smoke passed with 127 database-backed rows; run the native Windows or Android Players tab checks for count, real rows, search, and profile navigation. |
| **Sprint closeout and spec reconciliation** | `PLAYERS-6` | 2 | board `[x]` / task refs `[x]` / memory `[x]` / review `[x]` / native smoke `[ ]` | Automated verification, spec reconciliation, and review are complete; final closure waits only for native MAUI smoke evidence. |

### In review

_(none)_

### Done

| Card | Story | Pts | Evidence |
|------|-------|----:|----------|
| **Players contract and data-shape inventory** | `PLAYERS-0` | 2 | Existing `PlayerDirectoryDto`, `PlayerDirectoryEntryDto`, and `PlayerSummaryDto` reused; route selected as `GET players/directory`; private identity/contact fields excluded. |
| **Backend players directory query and endpoint** | `PLAYERS-1` | 5 | Added `IPlayerProfileRepository.ListDirectoryAsync`, privacy-safe EF projection from active `PlayerProfiles`, `GetPlayerDirectoryQueryHandler`, `PlayersFunctions.GetPlayerDirectory`, and DI registration. |
| **MAUI `ApiPlayersClient` and API-mode registration** | `PLAYERS-2` | 3 | Added `ApiPlayersClient`; API mode now resolves `IPlayersClient` to API while Seed mode keeps `SeedPlayersClient`. |
| **Players tab API-mode behavior tests** | `PLAYERS-3` | 3 | Added API client route/response tests and registration tests; existing `PlayersPageModelTests` continue to cover load, search, refresh-related state, empty, offline, error, and navigation behavior through `IPlayersClient`. |
| **Player profile navigation follow-through** | `PLAYERS-4` | 3 | Added `GetPlayerProfileQueryHandler`, `GET profiles/{playerProfileId:guid}`, and `ApiProfileClient.GetProfileAsync`; tests cover route metadata, not-found handling, stats/recent-form mapping, and profile route calls. |

### Blocked

_(none)_

## Requirements checklist

- [x] Players tab uses `ApiPlayersClient` in API mode.
- [x] Seed mode still uses `SeedPlayersClient`.
- [x] Directory rows come from active, non-deleted `PlayerProfiles`.
- [x] Internal identity ids and contact data are excluded from the public directory contract.
- [x] No phone numbers, emails, tokens, payment identifiers, or waiver details are returned by the directory endpoint.
- [x] `PlayersPage.xaml` remains aligned to `documentation/mobile-wireframes.html`.
- [x] `PlayersPageModel` keeps depending on `IPlayersClient`, not raw HTTP or backend types.
- [x] Backend, client, and page-model tests cover the slice.
- [ ] Local API-mode smoke confirms real database players appear in the running MAUI app.
- [x] Windows and Android builds pass with no new warnings.

## Review notes from sprint creation

- `IPlayersClient.GetDirectoryAsync()` already exists and returns `PlayerDirectoryDto`.
- API mode currently registers API-backed profile/session admin clients but still relies on the seed
  players client through the shared seed fallback registration.
- `ApiProfileClient.GetProfileAsync(Guid)` currently returns null, so profile navigation is part of
  this sprint if the Players tab row tap must be end-to-end.
- The backend repository currently supports individual profile lookup and mutation, but not a
  directory query.
- Sprint 03 remains parked and should be resumed from
  [`sprint-03-board.md`](sprint-03-board.md) after this focused Players tab sprint.
- Added [`sprint-04-local-db-smoke.md`](sprint-04-local-db-smoke.md) plus HTTP smoke requests in
  `http/ProfileFunctions/profiles.http` and `http/00-local-smoke/local-m9-sequence.http`.

## Closeout evidence

- Local authenticated API smoke: directory and profile routes passed; 127 database-backed player
  rows returned; no phone, email, token, payment, waiver, or emergency-contact fields observed.
- Automated tests: Domain 1, Application 64, Infrastructure 55, Functions 98, Client 381.
- Added explicit coverage for repository projection/soft deletes, Function response serialization,
  RFC 7807 client errors, request cancellation, refresh recovery, and page-model cancellation.
- Dependency audit: no vulnerable packages reported for the Functions dependency graph.
- MAUI builds: Windows and Android passed with zero warnings and zero errors.
- Final review: automated implementation is clean; closure remains pending the required native MAUI smoke.

## How to keep this current

1. Move one card at a time into **In progress** as implementation starts.
2. Flip task fragments from `[ ]` to `[~]` while work is active and `[x]` when evidence exists.
3. Move to **In review** only after implementation and tests are ready for review.
4. Move to **Done** only after the Definition of Done in the sprint spec is satisfied.
5. Record blockers with the exact endpoint, schema, contract, or wireframe conflict.
