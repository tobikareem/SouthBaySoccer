# TEAM-5 Design

Anchored to wireframe screens `captains` (rank badges) and `draft` (turn banner + auto-balance).

## Server

- **Caps and turn are server-owned policy** (`GameDayWorkflowQueries.ComputeTeamCaps` /
  `ResolveDraftTurn`): caps split the eligible roster evenly with the remainder to the
  highest-ranked teams; the turn is derived statelessly by replaying the snake sequence over the
  recorded non-captain pick counts, skipping full teams. `TeamDraftDto` carries
  `TeamCaps`/`OnTheClockTeamId`/`OnTheClockLabel`/`IsMyTurn`/`CanAutoBalance`; the client renders,
  never recomputes (its old local split survives only as an old-API fallback).
- **`DraftPickCommand`** (`POST game-day/sessions/{id}/teams/picks`): one player, on-the-clock team
  only; captain of that team or a game admin acting for them. Audit `TeamDraft.Pick`.
- **`SaveCaptainTeamPicks`** is now game-admin only (the correction tool).
- **`AutoBalanceTeamsCommand`** (`POST game-day/sessions/{id}/teams/auto-balance`, body
  `{ attempt }`): `EnsureGameAdmin`; match must be **Draft** (stricter than `EnsureTeamsEditable` —
  never through the post-game correction window); every team needs a captain and the roster must
  cover the teams. Scores = `(sumVotes + K·rosterMean) / (voteCount + K)`, K=4, votes only from
  settled matches (`IStatsRepository.ListPlayerRatingAggregatesAsync`). Deal =
  `TeamBalancer.Balance` (pure): captains seeded on ranked teams, seeded tie-shuffle
  (FNV-1a(matchId) XOR attempt), strict snake fill, ≤50 best-swap improvement passes (ε 0.001).
  **Postconditions verified before writing** (every eligible player exactly once, captains on their
  ranked teams, team sizes equal projected caps, no outsiders) — a violation is an invariant bug
  and aborts. Write = `ReplaceAllTeamAssignmentsAsync` (MatchTeams untouched, churn-free reuse,
  PlayerMatchStats reconciled). Audit `TeamDraft.AutoBalance` with the full deal in DetailsJson.
- Determinism: same (matchId, attempt) ⇒ same deal, so idempotent retries rewrite identical rows;
  a new attempt is a visibly different deal.

## Client

- Captains page: tap order tracked as rank (`CaptainPlayerItem.RankLabel` badges), grant payload in
  rank order, rank changes re-enable Grant.
- Draft page: turn banner (`NoticeSurface`); captains tap-to-pick on their turn (server call +
  reload) and never see the bulk Save; admins keep multi-select + Save, the team switcher, and the
  Auto-balance button (confirm dialog; each run bumps the attempt).

## Concurrency (review hardening)

Every draft mutation — snake pick, admin bulk correction, auto-balance, captain grant, lock,
unlock — runs its read-check-write core inside `IUnitOfWork.ExecuteInSerializableTransactionAsync`
(the `RsvpRepository` capacity-race pattern promoted onto the unit of work): serializable isolation
range-locks the guards' reads, deadlock/serialization victims retry with a cleared change tracker,
and exhausted retries surface as a 409, never a 500. This closes the two races found in review —
double-consumption of a snake turn, and auto-balance replacing assignments after a concurrent lock
committed InProgress.

Two further review outcomes:
- **Bulk correction obeys caps**: `SaveCaptainTeamPicks` rejects payloads above the team's
  server-computed cap, so an admin can no longer lock overfilled teams.
- **The deal number is server-owned**: `Match.AutoBalanceVersion` increments inside the balance
  transaction and seeds the shuffle; the client sends no attempt at all, so page reopens, transient
  failures, and multiple admins can never replay or skip a variant.

## Deliberate limits (v1)

- Rating-only balance — no position/goalkeeper spreading (PreferredPosition is sparse free text).
- All-time ratings, not season-scoped.
- No draft undo command — the admin bulk save is the correction tool.
- `System.Random` determinism is per-runtime-version, which is sufficient: idempotent replay is
  same-deployment by construction.
