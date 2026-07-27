# Performance Review & Optimization Plan — 2026-07-26

Staff-level performance review of the whole solution: the Azure Functions backend (`src/`) and the
MAUI client (`SouthBaySoccer/`). Produced by three parallel audits (backend, client,
measurement/constraints) plus an implementation-design pass, with every cited claim verified against
source at commit `170fe8c` and a live production baseline pulled from Application Insights.

> This file lives in `_specs/perf/` deliberately: `documentation/**` is published publicly by
> `.github/workflows/pages.yml`, and this report contains production telemetry.

---

## 1. Executive summary

1. **The worst user-visible latency is not code — it is the paused database.** Production is Azure
   SQL **serverless `GP_S_Gen5` with auto-pause enabled**; at review time (2026-07-26) the database
   status was literally `Paused`. First request after idle pays a 30–60s resume: `Refresh` p95 =
   26.0s (65 of 169 calls failed), `SignInByPhone` p90 = 15.0s, and ~15s maxima on nearly every
   endpoint. `_specs/sprint-02-data-model.md:448` flagged this as a production blocker; it was never
   resolved. Fixing it is an ops decision (§5, Phase 0) and costs zero code.
2. **The two hottest GETs serialize behind an inline external import.** `GET sessions` (781 calls,
   p50 27ms) and `GET game-day/today` (322 calls, p50 98ms) go bimodal — 20–25% of calls take ≥1s —
   because the Pickup Pal refresh runs inline, its freshness check sits *inside* a semaphore, and
   the import itself is an N+1 storm (~425 sequential DB round trips worst case).
3. **There is no caching anywhere** — backend or client. No `IMemoryCache`, no response cache, no
   ETags, and the MAUI client refetches every screen on every tab switch (Sessions Home = 4 requests
   each time) with 2 SecureStorage decrypts per HTTP call.
4. **Several queries scale wrong for the future**: the leaderboard runs 6–7 correlated subqueries
   per player over 4 unindexed columns; the players directory is an unpaginated full-table scan with
   a per-row subquery; the serializable RSVP transaction holds locks across 7+ round trips with a
   per-waitlist-entry eligibility N+1.
5. Current absolute numbers at today's data volume are mostly fine (warm p50s of 16–100ms). The plan
   below removes the tail (items 1–2), the chattiness (item 3), and the future cliffs (item 4),
   phase by phase, each independently shippable and guarded against regressions.

---

## 2. Measured baseline (production, 30 days to 2026-07-26)

Source: Log Analytics workspace `southbaysoccerbay-resource-logs` (App Insights component
`southbaysoccerbay-resource-appinsights`, resource group `SouthBaySoccer_Rg`). Request telemetry is
**unsampled** (`host.json` excludes `Request` from sampling), so counts are exact.

### 2.1 Request latency per operation (top by volume, DurationMs)

| Operation | Count | p50 | p90 | p95 | Max | Failures |
|---|---:|---:|---:|---:|---:|---:|
| ListUpcomingSessions | 781 | 27 | 2,095 | 2,324 | 15,471 | 13 |
| GetMyProfile | 493 | 17 | 96 | 254 | 2,380 | 0 |
| GetPendingStatSubmission | 464 | 34 | 236 | 264 | 469 | 0 |
| GetTodayGameDayContext | 322 | 98 | 1,893 | 2,638 | 15,010 | 7 |
| GetSeasonLeaderboard | 301 | 43 | 268 | 369 | 15,039 | 4 |
| GetMyRsvp | 271 | 16 | 100 | 179 | 271 | 0 |
| **SignInByPhone** | 197 | **1,158** | **15,013** | 15,466 | 27,894 | **23** |
| **Refresh** | 169 | **2,399** | **15,458** | **25,985** | 29,194 | **65** |
| GetMyGroups | 129 | 408 | 1,259 | 1,501 | 1,726 | 0 |
| GetPlayerDirectory | 115 | 17 | 102 | 192 | 2,044 | 0 |
| GetRecentGames | 98 | 37 | 157 | 350 | 2,393 | 0 |
| GetPlayerProfile | 74 | 25 | 295 | 966 | 2,811 | 0 |

### 2.2 Duration distribution (confirms bimodality)

| Operation | &lt;100ms | 100ms–1s | 1–3s | 3–10s | &gt;10s | Share ≥1s |
|---|---:|---:|---:|---:|---:|---:|
| ListUpcomingSessions | 475 | 147 | 133 | 17 | 9 | 20% |
| GetTodayGameDayContext | 162 | 79 | 69 | 7 | 5 | 25% |
| GetSeasonLeaderboard | 176 | 117 | 4 | 0 | 4 | 3% |
| GetMyGroups | 4 | 105 | 20 | 0 | 0 | 16% |
| SignInByPhone | 5 | 86 | 52 | 23 | 31 | 54% |
| Refresh | 25 | 35 | 42 | 8 | 59 | **64%** |

Interpretation:
- The fast modes prove the queries themselves are cheap at today's volume; the slow modes line up
  with (a) DB auto-pause resume + Functions cold start (`Refresh`/`SignInByPhone` are the first
  calls an opening app makes) and (b) the inline Pickup Pal refresh window (`ListUpcomingSessions`
  / `GetTodayGameDayContext` 1–3s bucket ≈ the import; `GetMyGroups` pays the external call on
  every single request — it has essentially no &lt;100ms mode).
- `Refresh` failing 65/169 (38%) with a 26s p95 against the client's 30s timeout means stored-session
  restore frequently dies and users get bounced to sign-in — a UX bug caused by latency.

### 2.3 Infrastructure facts confirmed via Azure (read-only)

- Function App `SouthBaySoccerFunc`, RG `SouthBaySoccer_Rg`, westus2, plan `ASP-SouthBaySoccerRg-a65f`
  (SKU/OS not yet confirmed — do this before choosing a ReadyToRun RID, Phase 6b).
- SQL: server `southbaysoccer-prod-sql`, DB `SouthBaySoccerProdDb`, **`GP_S_Gen5` serverless,
  capacity 2, status observed `Paused`**.
- **`AppDependencies` is empty** — only host-level request telemetry is collected
  (`AddApplicationInsightsTelemetryWorkerService()` is never called in `Program.cs`). SQL-per-request
  ratios can NOT be measured from App Insights today. Do not "fix" this by enabling dependency
  tracking casually: `.ai/memory/pickuppal-phone-sign-in.md` forbids outbound HTTP instrumentation
  until a URL-redaction rule exists (raw phone number in the Pickup Pal lookup URL path).

### 2.4 Re-run instructions (append results here after each phase)

Workspace: `southbaysoccerbay-resource-logs`. Queries used for this baseline:

```kusto
// Latency per operation
AppRequests | where TimeGenerated > ago(30d)
| summarize Count=count(), P50=percentile(DurationMs,50), P90=percentile(DurationMs,90),
            P95=percentile(DurationMs,95), MaxMs=max(DurationMs), Failures=countif(Success == false)
  by OperationName | order by Count desc
```

```kusto
// Distribution buckets for the hot endpoints
AppRequests | where TimeGenerated > ago(30d)
| where OperationName in ('ListUpcomingSessions','GetTodayGameDayContext','SignInByPhone','Refresh','GetMyGroups','GetSeasonLeaderboard')
| extend Bucket = case(DurationMs < 100,'a_under100ms', DurationMs < 1000,'b_100ms-1s',
                       DurationMs < 3000,'c_1-3s', DurationMs < 10000,'d_3-10s','e_over10s')
| summarize Count=count() by OperationName, Bucket | order by OperationName asc, Bucket asc
```

```kusto
// Daily p95 trend for before/after overlays
AppRequests | where TimeGenerated > ago(14d)
| where OperationName in ('ListUpcomingSessions','GetTodayGameDayContext','SubmitRsvp','GetSeasonLeaderboard','GetPlayerDirectory','GetMyProfile','GetMyGroups','Refresh','SignInByPhone')
| summarize P95=percentile(DurationMs,95), N=count() by OperationName, bin(TimeGenerated, 1d)
```

Round-trip-count verification (since `AppDependencies` is empty) happens in tests instead:
Infrastructure.Tests on LocalDB can assert query counts via an EF `DbCommandInterceptor` counter, and
the client request-count tests (§Phase 0) pin HTTP call counts per screen.

---

## 3. Root causes — backend (ranked)

Hot paths: 81 HTTP endpoints, no timers/queues. All references verified.

| # | Finding | Where | Severity |
|---|---|---|---|
| B1 | DB auto-pause resume dominates tail latency; also conflates every "cold start" measurement | Azure SQL `GP_S_Gen5`, status `Paused` | **Critical (ops)** |
| B2 | Freshness check *inside* the refresh semaphore: every `GET sessions` / `game-day/today` request queues behind an in-flight ≤5s import; import inline on request path | `Functions/Sessions/GameDayPickupPalRefreshService.cs:24-28` | **High** |
| B3 | Import N+1 storm: per game 4 lookups incl. re-reading ALL venues per game; per participant up to 4 sequential profile lookups; ~425 RTTs for 5 games × 20 players | `Application/Features/Scheduling/ImportPickupPalGamesCommandHandler.cs:95-259,370-390` | **High** |
| B4 | RSVP serializable txn: ≥7 RTTs holding range locks; cancel+promote awaits waiver eligibility (2 queries) per waitlist entry *inside* the txn; nested retry (3× outer × 6 EF) | `Infrastructure/Repositories/RsvpRepository.cs:17-100,364-395,503-533` | **High** |
| B5 | Leaderboard: 6–7 correlated subqueries per player, `eligibleMatches` join re-inlined 7×, ORDER BY computed aggregates before Skip/Take | `Infrastructure/Repositories/StatsRepository.cs:395-448,519-588` | **High (scales badly)** |
| B6 | Missing indexes: `PlayerRatingVotes(RatedPlayerProfileId)`, `PlayerLikes(ReceiverPlayerProfileId)`, `MatchAwards(PlayerProfileId)`, `Matches(Status)`, `MatchEvents` ReviewStatus/assist coverage, `PlayerProfiles(NormalizedDisplayName)`, `CheckIns` Outcome coverage | `Persistence/SouthBaySoccerModelConfiguration.cs:111-124` + snapshot | **High (with B5)** |
| B7 | Uncached reflection on 100% of requests: `AppDomain.GetAssemblies()` probe + `GetMethod` + 4 `GetCustomAttribute` per request; singleton but no memo | `Functions/Pipeline/ReflectionEndpointPolicyResolver.cs:19-37` | Medium (universal) |
| B8 | Zero caching: no IMemoryCache/ETags anywhere; seasons/venues/current-waiver/group-catalog re-queried per request; `GroupHandlers.cs:22-25` even documents the intended cache | whole `src/` | **High** |
| B9 | `GET game-day/today` does 7 RTTs *per candidate session* in a loop (+1 venue query each); a batched projection (`SessionAttendanceProjection.Build`) already exists and is used by the sessions feed | `RsvpRepository.cs:317-362`; `GameDayHandlers.cs:158-164,261-273` | **High** |
| B10 | Pickup Pal HttpClients: no timeout (100s default), no retry; `GetMyGroups`/`GetAvailableGroups` call the provider per request uncapped + per-group N+1 upserts | `Infrastructure/DependencyInjection.cs:71-73`; `GroupHandlers.cs:25,85,104-124` | **High** |
| B11 | `JwtTokenService` scoped but stateless: rebuilds signing-key index + re-validates options per request; new `HMACSHA256` per sign | `DependencyInjection.cs:80`; `Authentication/JwtTokenService.cs` | Medium |
| B12 | Players directory: full-table read, correlated `PlayerMatchStats` count per row, no paging | `PlayerProfileRepository.cs:55-70` | Medium (grows) |
| B13 | Sync EF `Queryable.Any()` per row in loops (blocking round trips), admin merge route | `StatsRepository.cs:615-638` + call sites | Medium |
| B14 | Idempotency store: 3 extra RTTs + 2 extra `SaveChanges` outside UnitOfWork; response serialized twice | `Pipeline/IdempotentRequestExecutor.cs`; `Idempotency/EfIdempotencyStore.cs` | Medium |
| B15 | Cold start: non-pooled `AddDbContext` (scoped interceptor blocks naive pooling), no ReadyToRun/TieredPGO, full Identity+DataProtection registered but barely used, stale `UserSecrets 6.0.1` package, EF command text shipped to App Insights (no `logging.logLevel`) | `DependencyInjection.cs:47-52,85-105`; Functions csproj; `host.json` | Medium |
| B16 | 15 `DbSet.Update` whole-entity marks; worst rewrites the large `SanitizedGameJson` blob every import pass | `PickupPalGameRepository.cs:21-22,78-79` etc. | Low-Med |
| B17 | Zero `AsNoTracking` (entity-returning read methods pay tracking); zero `Include` (good — manual joins/projections) | repo-wide | Low-Med |
| B18 | Outbox is write-only — `OutboxMessages` grows unbounded, nothing consumes it | `RsvpRepository.cs:535-556` | Flag (correctness) |

## 4. Root causes — client (ranked)

| # | Finding | Where | Severity |
|---|---|---|---|
| C1 | Refetch-on-every-`Appearing` on 15 pages; Shell keeps tabs alive so every tab switch = full reload (Sessions Home = 4 requests; Schedule repeats 2 of them); no client cache of any kind | `SessionsHomePageModel.cs:105-109`, `PlayersPageModel.cs:61-65`, etc. | **High** |
| C2 | 2 SecureStorage decrypts per HTTP request (token + expiry), no in-memory token copy | `AuthenticationSessionRefresher.cs:98,104`; `AuthenticationHandler.cs:17` | **High** |
| C3 | `GetSessionAsync` downloads the ENTIRE sessions feed then filters client-side; session detail = feed + roster; RSVP tap = third fetch | `ApiSessionsClient.cs:62-67` | **High** |
| C4 | 31 `BindableLayout` (non-virtualizing) lists vs 6 `CollectionView`; worst: full players directory inside `ScrollView` at ~20 views/row | `PlayersPage.xaml:84`; `Controls/PlayerRow.xaml` | **High** |
| C5 | `PlayerRow`/`Avatar` controls have no `x:DataType` → ~14 reflection bindings per row (feature pages are compiled — the controls are the anomaly) | `Controls/*.xaml` | Medium-High |
| C6 | Search re-projects the entire directory per keystroke, no debounce (a good debouncer pattern already exists in `CreateSessionPage.xaml.cs:7-120`) | `PlayersPageModel.cs:59,102-133` | Medium |
| C7 | No Android profiled AOT / trimming settings at all; 12.7MB fonts (Font Awesome Brands 563KB unused; SegoeUI 870KB Windows-only face shipped everywhere) | `SouthBaySoccer.csproj` | Medium |
| C8 | No gzip negotiation; reflection STJ for API DTOs (source-gen `JsonContext` exists only for seed data); no GET retry despite existing idempotency keys | `ClientServiceCollectionExtensions.cs` | Medium |
| C9 | Only `SchedulePageModel.cs:95-99` avoids blanking to `Loading` on refresh — every other page flashes a spinner on every tab switch | page models | Medium (perceived) |
| C10 | `ObservableCollection` `Clear()`+per-item `Add` loops; `PropertyChanged` subscribed in a rebuild loop with no unsubscribe (leak + duplicate handlers) | `GameDayPageModels.cs:836-845,995-1035,1270-1282` | Medium |
| C11 | Positive: startup path is clean — no blocking calls, ANR fix intact (`WelcomeBackPage.xaml.cs:25-36`), correct MainThread usage, 30s timeout, tested 401 replay. Do not regress these. | — | — |

---

## 5. Phased implementation plan

Each phase is independently shippable and revertible. Order = value ÷ risk.

### Phase 0 — Ops decision + baseline lock-in + guard scaffolding (no product code)

**0.0 SQL auto-pause decision (the #1 tail-latency lever).** Current config (verified 2026-07-26):
`GP_S_Gen5` serverless, max 2 vCores, **min 0.5 vCores, auto-pause after 60 min idle**. Verified
westus2 retail rates: serverless compute **$0.5218/vCore-hour**; Standard DTU **S0 $0.4839/day,
S1 $0.9677/day, S2 $2.42/day** (storage up to 250GB included in DTU tiers).

| Option | Est. compute cost / month | First-open latency |
|---|---|---|
| Do nothing (today) | ~$16–32 (awake ~2–4 h/day at the 0.5-vCore floor) | 30–60s resume + Refresh failures |
| **A (recommended): switch to Standard S1 (20 DTU)** | **~$29 flat** | none — always on |
| A′: Standard S0 (10 DTU) — try after Phase 3 shrinks query cost | ~$15 flat | none — always on |
| B: stay serverless, disable auto-pause (`--auto-pause-delay -1`) | ~$190+ floor (0.5 vCore × 730 h) | none |
| C: keep auto-pause + Functions `TimerTrigger` warm-up ping 06:00–23:00 PT | ~$135 + code to maintain | none in window; resume off-hours |

Serverless only saves money while paused — which is exactly what causes the pain. Always-on S1
costs ~6× less than always-on serverless at the current floor, so if the answer is "always on",
the DTU tier is the rational shape. Trade-offs: S1 has less headroom than 2 serverless vCores
(warm p50s today are 16–100ms; Phases 2–3 remove most query cost — if S1 ever feels tight, S2 at
~$74 still beats option B), and the tier change is an online operation with a brief connection
blip at cutover (absorbed by `EnableRetryOnFailure`), reversible anytime:
`az sql db update -g SouthBaySoccer_Rg -s southbaysoccer-prod-sql -n SouthBaySoccerProdDb --service-objective S1`.
Whatever is chosen, re-run §2.4 two weeks later; `Refresh`/`SignInByPhone` tails are the metric.

**0.1 Record this baseline** — this file is the record; append re-runs under §2.4.

**0.2 Client request-count harness.**
- Create `tests/SouthBaySoccer.Client.Tests/TestSupport/CountingHttpMessageHandler.cs` (consolidates the 5 duplicated `StubHttpMessageHandler`s; records `(method, path)`).
- Create `ScreenRequestCountTests.cs` pinning today's per-screen request sets (SessionsHome = 4, Schedule = 2, etc.). Phase 5 updates these expectations deliberately — that's the cache regression guard.

**0.3 Windows CI job for Infrastructure.Tests** (currently ZERO CI coverage — they're LocalDB-only and excluded everywhere):
- `.github/workflows/infrastructure-tests.yml`: `on: pull_request` (paths `src/**`, `tests/SouthBaySoccer.Infrastructure.Tests/**`), `runs-on: windows-latest`, setup .NET `10.0.301`, `sqllocaldb start MSSQLLocalDB`, `dotnet test`.
- Fix the known fixture blind spot while here: `InfrastructureDatabaseFixture` must enable `EnableRetryOnFailure()` to match production (`.ai/lessons/2026-07-21-ef-retry-strategy-manual-transactions.md`).

### Phase 1 — Backend request-path unblocking (no schema, no contracts, no cache)

**1.1 Lock-free freshness fast path** — `GameDayPickupPalRefreshService.cs`. Keeps single-flight,
1-min freshness, 5s budget, fail-open (all required by `.ai/memory/game-day-today-projection.md`);
removes only the *waiting*:

```csharp
// fast path: fresh => return without touching the gate
if (IsFresh(Volatile.Read(ref lastAttemptTicksUtc), clock.UtcNow)) return;
// stale: only ONE request imports; everyone else serves persisted data immediately
if (!await refreshGate.WaitAsync(0, cancellationToken)) return;   // fail-open by design
try { /* re-check freshness, Volatile.Write timestamp, run import with 5s CTS as today */ }
finally { refreshGate.Release(); }
```
Guard: new `GameDayPickupPalRefreshServiceTests` (concurrent callers don't block behind a slow fake
import; fresh window suppresses; failed attempt still stamps the throttle).
Impact: removes the up-to-5s stall from 20–25% of `GET sessions` / `game-day/today` calls.

**1.2 Memoize endpoint policy resolution** — `ReflectionEndpointPolicyResolver.cs`: wrap `Resolve`
in a `ConcurrentDictionary<string, EndpointAccessRequirement>.GetOrAdd`. Cache successes only —
`EndpointClassificationException` still throws every time (fail-closed unchanged). Entry points are
a fixed compile-time set; no invalidation needed. Guard: `EndpointPolicyResolverTests` stays green +
one memoization test.

**1.3 `JwtTokenService` → singleton** (deps are singleton-safe: `IOptions`, `IClock`); precompute key
bytes; use static `HMACSHA256.HashData(...)` instead of allocating per call. Guard: existing
sign→validate round-trip tests + a parallel-use smoke test.

**1.4 Pickup Pal HttpClient timeouts** — user client 10s, games client 5s, group client 10s. The
games client stays inside the import path's 5s budget; the user/group clients sit on interactive
sign-in and link flows, where 10s leaves headroom for the provider's own cold starts while staying
under the mobile client's 30s timeout. `GetMyGroups` (which previously rode an uncapped token) can
no longer hang 100s; handlers already fail open.

**1.5 `host.json` logging** — add `logging.logLevel`: `"Microsoft.EntityFrameworkCore": "Warning"` so
SQL command text stops shipping to App Insights. Keep `samplingSettings` exactly as-is (requests must
stay unsampled — it's our measurement).

### Phase 2 — Round-trip elimination (no schema changes)

**2.1 Batch the Pickup Pal import** — `ImportPickupPalGamesCommandHandler.cs` + small repo additions:
prefetch venues once per pass (dictionary, add-on-create); batch snapshots/sessions by game id /
occurrence key (`WHERE ... IN`, ≤3 queries); batch profile resolution (4 `IN` queries over user ids,
phone hashes, JID hashes, display names) feeding the existing `profileCache` with identical
precedence + ambiguity rules; skip the snapshot `Update` when `SanitizedGameJson` + scalars are
unchanged. Target: **~425 → ~12 round trips**. Guards: existing Application.Tests handler tests +
new cases (cross-game identity backfill, ambiguous display name stays snapshot-only, conflicting
user id rejected).

**2.2 Batch game-day attendance** — add `IRsvpRepository.GetGameDayAttendanceBatchAsync(sessionIds,
playerId)` built on 4 batched queries + the existing `SessionAttendanceProjection.Build` (the same
combined local+imported projection the capacity rule requires); rewrite the per-candidate loop in
`GameDayHandlers.cs:158-164`; hoist per-candidate venue lookups to one `ListByIdsAsync`. Single-session
method delegates to the batch. Target: `GET game-day/today` ~30+ → ~8 RTTs. Guard: Infrastructure test
asserting batch ≡ N single calls on seeded data.

**2.3 Slim the RSVP serializable transaction** (semantics unchanged: serializable isolation, ≤3
retries → 409, idempotency + outbox writes stay in-txn, capacity always computed from the combined
projection):
1. Merge `ListAttendanceEntriesAsync`'s 3 queries into 1 server-side `Concat` over a common
   anonymous shape (order on the anonymous projection — never after projecting to a positional
   record). Submit: 7 → 5 RTTs in-lock.
2. Prefetch promotion eligibility BEFORE the transaction (cancel path): batch waiver-acceptance for
   the session's waitlisted players in 2 queries outside the lock; pass a synchronous
   `Func<Guid,bool>` into `CancelAndPromoteAsync`. Entries that joined between prefetch and txn are
   skipped this pass but NOT expired. Cancel: 7+2N → ~7 RTTs flat. (This *shrinks* the serializable
   footprint — waiver tables stop taking range locks.)
3. Hoist `CreateExecutionStrategy()` out of the 3× retry loop (still inside
   `strategy.ExecuteAsync`, `ChangeTracker.Clear()` between attempts). Do NOT collapse the two
   retry layers — the outer loop's 409 taxonomy is contract.
**Critical guard:** new `RsvpConcurrencyTests` on LocalDB (retry-enabled fixture, Windows CI job):
capacity-1 session, two parallel `Going` submits → exactly one Going + one Waitlisted, never two;
cancel promotes exactly one eligible candidate.

### Phase 3 — Indexes + query rewrites that change scaling class

**3.1 Index migration** (controlled-migrations process: EF migration + regenerate
`artifacts/migrations/SouthBaySoccer.sql` idempotent script; nothing at startup):

| Table | New index | Filter |
|---|---|---|
| PlayerRatingVotes | `(RatedPlayerProfileId, MatchId)` INCLUDE `(Score)` | `[IsDeleted]=0` |
| PlayerLikes | `(ReceiverPlayerProfileId, MatchId)` | `[IsDeleted]=0` |
| MatchAwards | `(PlayerProfileId, AwardType, MatchId)` | `[IsDeleted]=0` |
| Matches | `(Status, SessionId)` | `[IsDeleted]=0` |
| MatchEvents | `(PlayerProfileId, EventType, ReviewStatus)` INCLUDE `(MatchId)` | `[IsDeleted]=0` |
| MatchEvents | `(AssistPlayerProfileId, EventType, ReviewStatus)` INCLUDE `(MatchId)` | `[IsDeleted]=0 AND [AssistPlayerProfileId] IS NOT NULL` |
| PlayerProfiles | `(NormalizedDisplayName)` | `[IsDeleted]=0` |
| CheckIns | `(SessionId, Outcome)` INCLUDE `(PlayerProfileId)` | `[IsDeleted]=0` |

**3.2 Leaderboard rewrite** — `StatsRepository.BuildPlayerStatAggregates`: materialize the eligible
match-id set once, then run **6 flat GROUP BY queries** (appearances/minutes, goals, assists,
ratings, likes, MVPs) each one round trip against the new indexes; merge into `PlayerStatAggregate`
in memory; apply the existing metric ordering + tie-breaks (display name, then id — contract) and
`Skip/Take` in memory. Stays derived-on-read from raw facts (no totals tables — the M9 rule);
ordering in memory sidesteps the positional-record EF trap. Player population is small (hundreds);
in-memory merge is trivial. Guard: golden-master test written against the OLD implementation first,
then swap; paging-contract test (default 25 / max 100).

**3.3 Players directory** — two queries (profiles ordered by now-indexed `NormalizedDisplayName`;
grouped `PlayerMatchStats` counts) merged in memory; response shape identical. Server+client
**pagination is a wire-contract change — deferred** to a separate decision.

### Phase 4 — Backend caching (see §6 for the full design)

`services.AddMemoryCache()` + small `Cached*Repository` decorators in
`Infrastructure/Repositories/Caching/` registered around the existing implementations. Per-instance
`IMemoryCache` deliberately (short TTLs bound cross-instance divergence; no new infra). Guards:
decorator hit/miss/invalidation tests; RSVP concurrency test re-run proves no cache leaked into the
write path.

### Phase 5 — Client caching, token cache, resilience

**5.1 In-memory access-token cache** inside the singleton `AuthenticationSessionRefresher`
(SecureStorage stays the durable store; memory is a read-through copy; refresh semaphore and
rejection semantics unchanged). Kills 2 SecureStorage decrypts per request. Guard: counting fake
`ISecureTokenStore` asserts zero reads on the warm path.

**5.2 Response cache decorators** over the typed API clients, registered only in `AddApiClients`
(Seed mode untouched; Correlation→Auth→ApiException handler chain untouched — decorators sit above
`HttpClient`). Core: a ~60-line singleton `ClientResponseCache` with
`GetOrCreateAsync(key, ttl, factory)`, `Invalidate(prefix)`, and stale-while-revalidate (return
stale immediately + `FireAndForgetSafeAsync` background refresh). Pull-to-refresh invalidates then
loads; any RSVP mutation invalidates `sessions:*`. Session detail resolves from the cached feed —
fixing the download-the-whole-feed problem without a new endpoint. Tab switches within TTL = **0
network requests** and no `Loading` blank (apply the `SchedulePageModel` state-suppression pattern
everywhere a cache serves). Guard: `ScreenRequestCountTests` expectations updated in the same PR
(first Appearing = 4; second within TTL = 0; pull-to-refresh = 4; post-mutation Appearing refetches).

**5.3 Gzip + GET retry** — `AutomaticDecompression` on the primary handler; standard resilience
retry for GET/HEAD only (mutations keep manual idempotency-key replay). Guard: `ApiPipelineTests`
extended with handler-order assertion.

### Phase 6 — Client rendering + build/publish

**6a Rendering (one PR, XAML tests updated together):**
- `PlayersPage.xaml`: `CollectionView` that **owns page scrolling** (header content moves into
  `CollectionView.Header` — never a vertical CollectionView inside a vertical ScrollView, per the
  documented Android ANR hazard in `SchedulePage.xaml:47-50`).
- Add `x:DataType` compiled bindings to `PlayerRow.xaml` / `Avatar.xaml`.
- Debounce directory search (250ms) reusing the `CreateSessionPage` CTS catch-up-loop pattern.
- Fix the `PropertyChanged` rebuild-loop leak in `GameDayPageModels.cs:1274`.
- Update `BrandUiLibraryTests`/`SessionScreensXamlTests` fixtures in the same PR (they assert on
  copied XAML by design).

**6b Build/publish (each independently shippable; iOS CI publish is fragile — scope per the
2026-07-25 lesson):**

| Change | Scoping | Validation before merge |
|---|---|---|
| Functions ReadyToRun | deploy workflow publish step only (`-p:PublishReadyToRun=true -r <rid>`) | confirm plan OS first; watch cold-start KQL post-deploy |
| Android profiled AOT | csproj `PropertyGroup` conditioned on Release + `-android` TFM | device cold-start stopwatch |
| Font pruning (Brands 563KB unused; SegoeUI 870KB non-Windows) | delete files + `AddFont` lines; grep style refs first | iOS TestFlight + Android visual pass |
| STJ source-gen for API DTOs | new `ApiJsonContext` following `Data/JsonContext.cs` pattern; start with Sessions+GameDay clients | 439 client tests already exercise deserialization |
| Bump `UserSecrets` 6.0.1 → 10.x (Functions) | backend only | CI build |

---

## 6. Caching design (what to cache, and what must never be cached)

### Backend (per-instance `IMemoryCache` decorators)

| Cache | Key | TTL | Invalidation | Staleness rationale |
|---|---|---|---|---|
| Active seasons | `seasons:active` | 15 min | remove on season create/update | changes a few times a year |
| Active venues | `venues:active` | 5 min | remove on venue create + after import creates one | admin/import-written picker data |
| Current waiver document | `waiver:current-doc` | 5 min | remove on waiver publish | read path only — write-path eligibility never reads this cache |
| Pickup Pal group catalog | `pickuppal:groups:catalog` | 5 min (serve stale ≤30 min on provider error) | TTL only | player-agnostic; fail-open improves resilience; code comment already anticipates it |
| Leaderboard page | `lb:{seasonId}:{metric}:{skip}:{take}:{groupChatId?}` | 60 s | TTL only | short-TTL response memoization, NOT a projection store — raw facts stay the only source of truth (M9 rule honored; call this out in the PR) |
| Players directory | `players:directory` | 60 s | remove on profile create/update/merge | tolerates a minute |
| Endpoint policy resolutions | entry-point string | process lifetime | never (code-derived) | immutable per binary |

### Client (memory, inside decorators over the typed clients)

| Cache | Key | TTL / stale-while-revalidate | Invalidation |
|---|---|---|---|
| Sessions feed (backs Home, Schedule, Session Detail) | `sessions:feed` | 30 s / 2 min SWR | any RSVP/waitlist mutation; pull-to-refresh |
| Pending stats prompt | `stats:pending-prompt` | 60 s | stat submit; pull-to-refresh |
| Profile (me) | `profile:me` | 5 min | profile update; pull-to-refresh |
| My groups | `groups:me` | 5 min | link-group mutation; pull-to-refresh |
| Players directory | `players:directory` | 60 s / 5 min SWR | pull-to-refresh |
| Leaderboard | `lb:{season}:{metric}` | 60 s | pull-to-refresh |
| Access token | in-object field | until expiry − 1 min skew | store/clear/rejection |

### DO-NOT-CACHE (regression tripwires)

- **RSVP capacity / attendance counts / waitlist state** — every capacity decision recomputes the
  combined local+Pickup-Pal projection live (a local-only or cached fast path is a correctness bug,
  not an optimization).
- **Waiver acceptance / payment eligibility on any write path** — compliance gates read live.
  Payment state generally: **Stripe (signed webhooks) is the source of truth — never cache or
  trust the DB/app memory for it.**
- **Auth material on the backend** — tokens, refresh tokens, challenge state, idempotency records.
- **`GET game-day/today` on the client** — live check-in counts; every Appearing stays a real request.
- **Anything keyed by raw phone / whatsappJid / groupId / subscriberId** — cache keys use internal
  GUIDs and existing SHA-256 hashes only.

---

## 7. Regression safety

**Must stay green in every phase (existing):** `StartupMigrationGuardTests`,
`EndpointPolicyResolverTests`, `HttpPipelineOrderTests`, all `*EndpointMetadataTests`, idempotency
tests, `ApiPipelineTests` (16), `AppStartupServiceTests` (9), the structural XAML tests (updated only
inside Phase 6a's PR), the full Domain/Application/Client suites (439 client tests).

**New guards added by this plan:** refresh-service concurrency tests (P1); import batching semantic
tests (P2); `RsvpConcurrencyTests` on LocalDB with a retry-enabled fixture (P0+P2); game-day
batch≡singles equality test (P2); leaderboard golden-master + paging-contract tests (P3); directory
old≡new equality test (P3); cache decorator hit/miss/invalidation tests (P4);
`ScreenRequestCountTests` (P0, re-pinned in P5); token-store read-count test (P5); debounce timing
tests (P6a).

**Per-phase checklist:** (1) `dotnet build` zero warnings + full `dotnet test`
(Domain/Application/Functions/Client run on macOS and ubuntu CI); (2) Infrastructure.Tests via the
new Windows CI job; (3) no wire-contract diff unless explicitly flagged (only candidate: directory
pagination — deferred); (4) re-run §2.4 KQL and append results here; (5) PII sweep on any new cache
key/log line (no raw phone/JID/group id); (6) each phase merges as one revertible unit.

**Known constraints honored** (from `.ai/memory` + `_specs`): controlled migrations (no startup
schema work); no mutable stat-totals tables; serializable RSVP semantics + combined-projection
capacity; execution-strategy transaction rules; test fixtures matching production retry config; no
outbound HTTP instrumentation without URL redaction; hashes-only for Pickup Pal identifiers;
pipeline order + fail-closed endpoint metadata; leaderboard paging contract; 5s/single-flight/
fail-open Pickup Pal isolation; client handler-pipeline and Seed/Api dual-mode integrity; deferred
Android session restore; no vertical CollectionView nested in vertical ScrollView.

---

## 8. Expected impact

| Phase | Expected effect |
|---|---|
| 0.0 | `Refresh`/`SignInByPhone` p95: ~26s → low seconds; the 38% Refresh failure rate should collapse; every other endpoint's >10s tail mostly disappears |
| 1 | `GET sessions`/`game-day/today` p95 during import windows: −up to 5,000ms (blocking removed); provider hangs 100s→5s; small fixed cost removed from all authenticated requests |
| 2 | Import ~425 → ~12 RTTs; game-day ~30+ → ~8 RTTs; RSVP in-lock 7→5 (submit) and 7+2N→~7 (cancel) — shorter locks ⇒ fewer serializable conflicts ⇒ fewer 409 retries (compounding) |
| 3 | Leaderboard: correlated-subqueries-×-players → 7 flat indexed queries (est. −70–90% p95; stops quadratic growth); directory scan → 2 indexed queries |
| 4 | Reference reads within TTL: −100% DB cost; leaderboard repeat views ~free; provider catalog deduped to 12/hour max per instance |
| 5 | Tab switches: 2–4 requests → 0 within TTL (biggest perceived client win); −2 SecureStorage decrypts per request; payloads −~70% with gzip |
| 6 | Players list render/scroll proportional to viewport; APK/IPA −≥1.4MB; Android cold start −20–40% with profiled AOT; Functions cold start improved by ReadyToRun (verify) |

## 9. Execution log

**2026-07-26 — Phase 0 (code parts) + Phase 1 shipped.**
- 0.2: `tests/SouthBaySoccer.Client.Tests/TestSupport/CountingHttpMessageHandler.cs` +
  `ScreenRequestCountTests.cs` (8 tests) pin per-screen request sets. Measured correction to §4 C1:
  Sessions Home is 4 requests on a fresh page model, then **2 per subsequent Appearing** (feed +
  pending-stats refetch every tab switch; profile/groups are memoized in instance fields).
- 0.3: `.github/workflows/infrastructure-tests.yml` (windows-latest, LocalDB) gives
  Infrastructure.Tests CI enforcement for the first time; `InfrastructureDatabaseFixture` now
  enables `EnableRetryOnFailure()` to match production.
- 1.1: `GameDayPickupPalRefreshService` — lock-free freshness fast path + zero-wait gate; 4 new
  concurrency/throttle tests.
- 1.2: `ReflectionEndpointPolicyResolver` — successful resolutions memoized; failures still throw
  (fail-closed); 2 new tests.
- 1.3: `JwtTokenService` → singleton; `Sign` uses static `HMACSHA256.HashData`.
- 1.4: Pickup Pal HttpClient timeouts (user 10s, games 5s, group 10s).
- 1.5: `host.json` — EF Core log category capped at Warning; sampling untouched.
- Gate: build 0 errors / 0 new warnings; Functions 121, Application 180, Domain 1,
  Infrastructure non-DB 18, Client 512 — all passing.
- Code review (dotnet-code-reviewer): no critical findings. Its four improvements were applied:
  parallel-use JWT smoke test; signing-key bytes precomputed in `JwtTokenService` (no per-call
  `GetBytes`); §1.4 text aligned to the shipped 10s/5s/10s timeouts; `permissions: contents: read`
  added to the new workflow.
- **Phase 0.0 executed 2026-07-26: production DB switched to Standard S1 (20 DTU, 30GB max,
  always-on).** Discoveries en route: 32GB is not a valid Standard max size (used 30GB), and the
  DB had `useFreeLimit: true` (Azure SQL free offer, `BillOverUsage`) despite the portal showing
  "Not Applied" — the free offer is serverless-only, so it was permanently opted out first.
  Baseline data before this date reflects the paused-serverless era; the §2.4 re-run (~2 weeks
  after the Phase 1 deploy) should show `Refresh`/`SignInByPhone` tails and failures collapsing.

### 2026-07-27 — Phase 2.2 + 2.3 implemented (2.1 not started)

- **2.2 Game-day batching.** `IRsvpRepository.GetGameDayAttendanceBatchAsync` added; the
  single-session method now delegates to it, so there is one code path. `GetTodayGameDayContext`
  replaced its per-candidate attendance loop (7 round trips each) and its per-candidate venue
  lookups with two batched reads; `GetRecentGames` batches venues via the new
  `IVenueRepository.ListByIdsAsync`. On a 3-game day: ~30 round trips → ~4.
- **2.3 RSVP transaction slimming.** `ListAttendanceRowsAsync` merges the three attendance reads
  into one `UNION ALL` round trip **for the read-only game-day batch only**. The transactional path
  keeps its three per-session queries on purpose: `SessionId == @id` holds a narrow index seek,
  whereas a collection `Contains` translates to an `OPENJSON` join whose plan can take much wider
  key-range locks under SERIALIZABLE — more costly in deadlocks than two saved round trips are
  worth. `CancelAndPromoteAsync` now takes a callback that is invoked **once with the whole
  waitlist** (`IPlayerSessionEligibilityService.CheckManyAsync` + the new `IWaiverRepository`
  `ListPlayerIdsWithCurrentAcceptanceAsync`), so cancel with an N-deep waitlist goes from 2N
  eligibility queries to **one**, still inside the transaction.
  - C3/C4/C5 held: capacity/waitlist decisions, the combined attendance projection, the outbox
    write, serializable isolation, and the ≤3-attempt→409 budget are unchanged and in-transaction.
  - Eligibility is read *inside* the transaction by design. An earlier revision prefetched it
    outside and was rejected in review: expiring a waitlist entry is irreversible, and a player who
    signed their waiver moments after the prefetch would have been expired on a stale verdict.
  - Still per-candidate: `IPaymentEligibilityService.CheckAsync` inside `CheckManyAsync`. Harmless
    today (the provider is a deferred no-op) but it needs its own batch method before M5 payments.
- Guards added: 2 Application tests (eligibility prefetched and passed; empty-waitlist path) and
  3 Infrastructure tests — **two players racing for the last spot** (exactly one Going, never two),
  batch-equals-per-session equivalence, and the skip-without-expiring rule. The first is the
  over-capacity proof Phase 2 required; all three run in the Windows CI job.
- Gate for 2.2/2.3 superseded by the combined Phase 2 gate below.
### 2026-07-27 — Phase 2.1 implemented (Phase 2 complete)

- **Import batching.** `ImportPickupPalGamesCommandHandler.PrefetchAsync` now reads everything the
  pass needs in ~8 batched queries into an `ImportLookups` bag: venues, snapshots by game id,
  sessions by id and by occurrence key, and profiles by PickupPal user id / phone hash / WhatsApp
  JID hash / normalized display name. Per-game and per-participant resolution then hits
  dictionaries only. New batch members on `IPickupPalGameRepository`, `ISessionRepository`,
  `IPlayerProfileRepository` (+ Infrastructure implementations). **5 games × 20 participants:
  ~425 → ~10 round trips.**
- Resolution parity is deliberate and exact: batches are ordered oldest-first and folded with
  `TryAdd`, so a hash matching several profiles picks the same row `FirstOrDefault(OrderBy
  CreatedAt)` returned; display names are grouped and a name matching more than one profile
  resolves to nobody, mirroring `FindSingleByNormalizedDisplayNameAsync`. `profileCache` still
  handles same-pass reuse, so which duplicates get created is unchanged.
- **One deliberate behavior fix:** venues created during a pass are now registered in the lookup.
  Previously two games at the same new location created two venue rows, because an added-but-unsaved
  venue is invisible to `ListActiveAsync`. Guarded by a new test.
- **Snapshot writes are skipped when nothing changed** (`SnapshotMatches`, deliberately excluding
  `CapturedAtUtc`). `SanitizedGameJson` holds the whole payload and `Update` marks every column
  modified, so an idle game used to rewrite that blob on every pass. Consequence to know:
  `CapturedAtUtc` now records the last content change, not the last poll.
- Guards added: ambiguous display name never links to either namesake; an unchanged payload writes
  no snapshot; two games sharing a new location create one venue. The first two stub the batch
  methods directly so they exercise the new grouping/change-detection rather than the harness
  fan-out.
- Known pre-existing quirk left intact (not introduced here): a participant whose identity keys
  differ between games (e.g. phone-only in game 1, user-id in game 2) still creates two profiles,
  because `BuildProfileCacheKeys` returns only the user key when a user id is present and the
  game-1 profile is not yet queryable. Worth revisiting separately.
- **Code review (dotnet-code-reviewer) found three blockers; all fixed before hand-off:**
  1. *Batch dictionaries were keyed on the value SQL returned, not the value requested.* SQL Server's
     `IN` ignores case and trailing spaces under the default collation, so a row could be returned by
     the query and still miss an exact-match dictionary — silently creating duplicate profiles and
     sessions. `IndexByRequestedKey` now matches on the requested key. This was a real defect: the
     new `HandleAsync_WhenStoredNameDiffersFromRequestByCaseOrTrailingSpace_...` theory fails against
     the pre-fix code. It is reachable today because `NormalizedDisplayName` is stored without a
     `Trim` but looked up with one.
  2. *`Contains` inside the serializable transaction* — reverted, see the 2.3 entry above.
  3. *Waitlist expiry acting on a pre-transaction verdict* — redesigned, see the 2.3 entry above.
  Also applied: deterministic `OrderBy(CreatedAt)` on the snapshot/occurrence-key batches (the
  single-key versions used `SingleOrDefault` and threw on duplicates; the batches must not pick
  arbitrarily), `ToLookup` in the game-day batch, and a race-test that treats a retry-exhausted
  409 as "not admitted" rather than failing CI for correct behaviour.
- Known-remaining, deliberately not in scope (raised in review): `GetRecentGamesQueryHandler` still
  issues per-session match/teams/events queries (venues are batched); `PrefetchAsync` inherits
  `ListActiveAsync`'s `Take(100)` cap, so a 100+ venue estate would resume duplicating venues — a
  targeted `ListByNamesAsync` would fix both correctness and cost.
- Gate: solution build 0 errors / 0 new warnings; Application 191, Functions 121, Domain 1,
  Client 512, Infrastructure non-DB 18 — all passing locally. The DB-backed Infrastructure tests
  (race, batch-equivalence, batched-eligibility) are macOS-unrunnable by design (C17) and are a
  hard merge gate in the Windows CI job — the EF translation of the new `Concat`/`Contains` queries
  is verified there and nowhere else.

### 2026-07-27 — Phase 3 implemented

- **3.1 Index migration** `20260727180445_PerfReadModelIndexes` adds the eight planned indexes
  (`Matches(Status,SessionId)`, two covering `MatchEvents` indexes for the goal and assist paths,
  `PlayerRatingVotes(RatedPlayerProfileId,MatchId)` INCLUDE `Score`,
  `PlayerLikes(ReceiverPlayerProfileId,MatchId)`, `MatchAwards(PlayerProfileId,AwardType,MatchId)`,
  `PlayerProfiles(NormalizedDisplayName)`, `CheckIns(SessionId,Outcome)` INCLUDE `PlayerProfileId`),
  all filtered `[IsDeleted] = 0` to match the global query filter. EF also drops the now-redundant
  single-column `IX_MatchEvents_AssistPlayerProfileId`, which the new composite supersedes.
  `artifacts/migrations/SouthBaySoccer.sql` regenerated (idempotent). Nothing runs at startup —
  `StartupMigrationGuardTests` stays green.
- **3.2 Leaderboard rewrite.** `BuildPlayerStatAggregates` (6 correlated subqueries *per grouped
  player*, each re-inlining the eligible-match join) is replaced by `ListPlayerStatAggregatesAsync`:
  one grouped query per fact type — appearances/minutes, goals, assists, ratings, likes, MVPs —
  merged in memory. The eligible-match set stays a subquery so SQL resolves it as a semi-join rather
  than a per-row lookup. Cost stops scaling with players × facts.
- Ordering and paging moved into `LeaderboardProjection` (new). This is not a behaviour regression:
  the old SQL ordered by computed aggregates, so it already had to evaluate every player before it
  could page — the page never reduced work. One deliberate difference: display-name tie-breaks now
  compare with `InvariantCultureIgnoreCase` instead of the database collation, so ordering no longer
  varies with server collation; the trailing player-id comparison keeps the order total either way.
- **3.3 Players directory** is two flat queries (profiles ordered by the newly indexed
  `NormalizedDisplayName`; match counts grouped once) merged in memory, replacing a correlated
  per-row `Distinct().Count()`. Response shape unchanged — pagination remains the deferred
  wire-contract decision.
- Guards: **`LeaderboardProjectionTests` (10 tests) pin every metric's tie-break chain and run on
  any machine**, because the ranking rules are now a pure function — that is the part of the
  rewrite most likely to drift. Three new DB-backed tests cover what the query layer changed and
  had no coverage for: full aggregate assembly across all five fact tables (ratings, likes, MVP
  included), `GetPlayerStatsAsync` agreeing with the same player's leaderboard row, and disjoint
  in-order pages. `SchemaContractTests` uses additive assertions, so the new indexes do not break it.
- **Code review found two blockers; both fixed before hand-off.** The reviewer independently
  reproduced all five new query shapes against EF 10.0.10 and confirmed they translate (notably
  `COUNT(DISTINCT …)` inside a `GroupBy`, and `eligibleMatchIds.Contains(…)` compiling to a real
  semi-join rather than a per-row lookup), and verified semantic parity including the assist
  correlation column and own-goal exclusion.
  1. *`GetPlayerStatsAsync` regressed.* `playerProfileId` was applied only to the base query, so the
     profile page aggregated the whole season's facts to serve one player — a regression on a hot
     endpoint, in a performance change. The optional filter is now pushed into all five fact
     queries; EF funcletizes it away when null, so the leaderboard SQL is unchanged.
  2. *No index on `PlayerMatchStats`* — the table the rewrite reads hardest. Added
     `(PlayerProfileId, MatchId) INCLUDE (Played, MinutesPlayed)` for the base grouping and the
     directory count, plus `Played` as an INCLUDE on the existing unique `(MatchId, PlayerProfileId)`
     so the goal/assist semi-join probe is covering.
  Also applied: `MatchAwards` re-keyed to `(AwardType, PlayerProfileId, MatchId)` because the MVP
  query always filters on `AwardType`; `NormalizedDisplayName` made covering so the directory's
  ordering does not fall back to a scan-plus-sort; the now-redundant `(PlayerProfileId, EventType)`
  index dropped rather than paying write amplification; and the directory's `Distinct()` removed
  (the unique `(MatchId, PlayerProfileId)` index already guarantees distinct matches per player).
- Latency shape to expect at the next measurement: the leaderboard's floor is now ~6 sequential
  round trips (they cannot be parallelized on a shared `DbContext`) instead of one large query, and
  `GetPlayerStatsAsync` ≈ 8. That trade removes the per-player subquery fan-out; watch it in §2.4.
- Test gaps the review caught, now closed: the `Played` semi-join guard and own-goal exclusion were
  both passing vacuously (every seeded player played every match, and own goals were only covered by
  an indirect assertion), and one tie-break case would have passed with its comparator deleted.
- Gate: solution build 0 errors / 0 new warnings; Application 191, Functions 121, Domain 1,
  Client 512, Infrastructure non-DB 28 — all passing locally. The DB-backed stats tests and the
  migration itself verify in the Windows CI job.

### 2026-07-27 — Phase 4 implemented (backend caching)

Mechanism: `AddMemoryCache()` plus `Infrastructure/Caching/`. Two shapes, chosen per target rather
than forcing one pattern:

- **Repository decorators** for reference lists that need write invalidation — `CachedSeasonRepository`
  (`seasons:active`, 15 min) and `CachedVenueRepository` (`venues:active`, 5 min), registered around
  the concrete repositories so the interface registration stays the only thing callers see.
- **`IReadThroughCache`** (Application abstraction, `MemoryReadThroughCache` in Infrastructure) for
  TTL-only response memoization at the handler, where the policy is visible at the use site:
  players directory (`players:directory`, 60 s) and the leaderboard page
  (`lb:{season}:{metric}:{skip}:{pageSize}:{group}`, 60 s). `IStatsRepository` has ~30 members, so a
  decorator there would have been delegation boilerplate obscuring one cached method.
- **`CachedPickupPalGroupClient`** (`pickuppal:groups:catalog`, 5 min fresh, serve-stale ≤30 min on
  provider error). Highest measured value in the phase: `GetMyGroups` had a p50 of 408 ms with
  effectively no sub-100 ms mode because every request made its own provider call for a list that is
  identical for every player. Serving stale on error also stops a Pickup Pal outage breaking sign-in.
- **Invalidation happens after commit, not at write time.** `CacheEvictionQueue` (scoped) collects
  keys and `UnitOfWork.SaveChangesAsync` drains it once the write is durable. Evicting at write time
  leaves a window in which a concurrent read repopulates from pre-commit state and pins stale data
  for a whole TTL; a request that never commits now correctly evicts nothing. Two tests pin both
  halves of that rule.
- Deliberately **not** cached: the current waiver document (spec listed it, but there is no waiver
  publish path in the app and `GET waivers/current` saw 1 call in 30 days — surface without benefit);
  `IVenueRepository.ListByIdsAsync` and `GetByIdAsync` (keyed by arbitrary id sets, would mostly miss);
  `GetLinkedGroupsAsync` (per-player link state). The §6 DO-NOT-CACHE list is otherwise untouched —
  no RSVP capacity, compliance, payment, or auth read goes through any cache added here, and every
  cache key carries internal identifiers only.
- Guards: 13 new tests — group-catalog hit/expiry/stale-on-error/throw-with-nothing-cached and
  "linked groups are never cached"; reference-repo hit, before-flush, after-flush, season
  invalidation, and "GetById is never served from the list cache"; read-through hit, key isolation,
  and "a failed factory poisons nothing". Handler tests use a `PassThroughReadThroughCache` fake so
  memoization never hides the repository interactions they assert.
- **Code review found three blockers; all fixed.**
  1. *The `venues:active` cache fed a **write** decision.* `PrefetchAsync` seeded the import's
     venue lookup from the cached list, and a miss there does not serve stale data — it INSERTS a
     venue. On a second instance a ≤5-min-old list would miss a venue this instance just created
     and duplicate the row, with no unique index on `Venue.Name` to stop it. Fixed by adding
     `IVenueRepository.ListByNamesAsync` (live, name-scoped, never cached) and moving the import
     onto it, which also drops the `Take(100)` cap the import inherited. Venue-name derivation is
     now a single `ResolveVenueName` helper so the prefetch and the create-or-reuse decision cannot
     disagree.
  2. *`catch (Exception) when (cached is not null)` swallowed caller cancellation*, returning a
     stale catalog as a normal result on host shutdown or an abandoned request. Now rethrows when
     `cancellationToken.IsCancellationRequested`, matching `GameDayPickupPalRefreshService`; an
     `HttpClient` timeout still falls through to stale because it leaves the caller's token
     unsignalled. A test pins the distinction.
  3. *Change-tracked EF entities in a process-wide cache.* Neither `ListActiveAsync` used
     `AsNoTracking`, so the singleton cache held entities attached to a long-disposed `DbContext`
     and handed the same mutable graph to every request for a whole TTL. Latent (no caller mutates
     a list-sourced entity today) but one line each to remove permanently.
  Also applied: **eviction is now drained by `AuditSoftDeleteSaveChangesInterceptor.SavedChangesAsync`
  instead of `UnitOfWork`**, so every commit path is covered by construction — seven services call
  `dbContext.SaveChangesAsync` directly, and any of them commits everything tracked in the shared
  scoped context. Season TTL cut 15 → 5 min, because on a second instance the longer window makes a
  newly created season invisible and *silently skips imports* rather than merely serving stale data.
- Known and accepted, recorded rather than fixed: the leaderboard read-through has no stampede
  protection (N concurrent misses at the TTL flip each cost ~6 sequential round trips); the players
  directory is TTL-only, not invalidated on profile writes as §6 says; and caching the group catalog
  means `ResolveGroupAsync` can 404 for a group created in the last 5 minutes. Each is worth a
  follow-up but none is a correctness break in the current single-instance deployment.
- Gate: solution build 0 errors / 0 new warnings; Application 191, Functions 121, Domain 1,
  Client 519, Infrastructure non-DB 43 — all passing locally.

### 2026-07-27 — Phase 5 partially implemented (client)

**Done:**
- **5.1 In-memory access-token copy** in the singleton `AuthenticationSessionRefresher`. Secure
  storage stays the durable record; the memory copy removes two platform-keystore decrypts from
  every authenticated request, which is a fixed cost every screen load multiplies. Populated on
  read and directly on refresh, and cleared on every definitive rejection path alongside
  `tokenStore.ClearAsync()`. The refresh semaphore and all rejection semantics are unchanged.
  3 tests: warm path reads storage once, an expired token inside the refresh skew is not served,
  and a refresh serves the new token without re-reading storage.
- **5.2 (partial) `ClientResponseCache`** + `CachedSessionsClient` (`sessions:dashboard`, 30 s),
  registered so the decorator owns the `ISessionsClient` registration while the
  correlation → auth → exception handler chain is untouched. Sessions Home and Schedule both call
  `GetDashboardAsync`, so the duplicate is gone and a tab switch inside 30 s costs no request. Any
  waitlist mutation invalidates `sessions:` — deliberately regardless of the reported outcome,
  because a timeout on a committed write would otherwise leave a stale feed. `GetSessionAsync` is
  left uncached: the detail screen is exactly where someone checks live capacity. 4 cache tests
  plus 2 updated registration assertions (the existing suite correctly caught the type change).

**Not done — remaining Phase 5 work:**
- Decorators for profile, groups, players directory, leaderboard, and the stats prompt.
- Pull-to-refresh must call `IClientResponseCache.Invalidate` before reloading, and sign-out must
  call `Clear()`. **Until that is wired, a decorated client's pull-to-refresh can serve cache
  inside the TTL** — acceptable for the 30 s dashboard, but it must land before the longer-TTL
  profile/groups caches (5 min) are added, and `Clear()` on sign-out is a correctness requirement
  before any per-player response is cached.
- `ScreenRequestCountTests` expectations still describe the pre-cache behaviour; they should be
  re-pinned (first Appearing = N, second inside TTL = 0) in the same PR that finishes the decorators.
- **5.3 gzip deliberately not applied.** The audit's finding assumed the managed handler default,
  but MAUI uses the native Android/iOS handlers, which already negotiate gzip. Forcing
  `ConfigurePrimaryHttpMessageHandler(new HttpClientHandler { AutomaticDecompression = … })` would
  replace the platform handler to fix something that may not be broken. Needs a device capture of
  the actual request headers before changing anything.
- GET-only retry not added yet.
- Gate: solution build 0 errors / 0 new warnings; Application 191, Functions 121, Domain 1,
  Client 519, Infrastructure non-DB 41 — all passing locally.

## 10. Decisions needed

1. **SQL auto-pause — RESOLVED 2026-07-26.** Switched to Standard S1 (~$29/mo flat, always-on); free-limit offer opted out (permanent). See §9 execution log.
2. **Per-instance IMemoryCache vs. Redis** — plan assumes per-instance (recommended at this scale; short TTLs bound divergence; zero new infra/cost). Revisit only if the plan fans out to many instances.
3. **Players-directory pagination** — wire-contract change, coordinated client+server PR pair; deferred (3.3's query fix removes the scan without it).
4. **Windows CI job** for Infrastructure.Tests (~3–5 min per PR) — the plan's concurrency proofs depend on it; recommended.
5. **Functions plan SKU/OS lookup** before the ReadyToRun RID is chosen (Phase 6b).
