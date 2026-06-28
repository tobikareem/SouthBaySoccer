# M9 - Leaderboards and Queries Plan

Implementation-ready plan for `_specs/tasks.md` M9. M9 turns the match-grain raw facts from M8 into
server-side read projections for leaderboards, profile career stats, and recent form. It does not
introduce maintained totals tables.

## Goal

Deliver paginated, deterministic query handlers and Function endpoints that derive stats from raw
rows:

- `Match -> Session -> Season` is the only season aggregation path.
- `PlayerMatchStats` contributes appearances and participation facts only.
- `MatchEvent` contributes approved goals and assists only.
- `PlayerRatingVote`, `PlayerLike`, and `MatchAward` contribute rating, likes, and MVP counts.
- `TeamAssignment + MatchResult` contributes profile recent form.
- Derived totals are computed on read and are never stored as mutable profile or leaderboard totals.

## Scope

### M9.1 - Query Projections

Implement `Features/Stats` query handlers for:

- season leaderboard by metric: goals, assists, rating, MVP;
- player career stats across all seasons;
- player season stats for one season;
- player recent form from published match results.

The handlers should depend on read-specific repository methods, not `GetAllAsync()` or exposed
`IQueryable`. Repository return types remain `IReadOnlyList<T>` or dedicated read models.

### M9.2 - Ordering, Tie-breakers, and Pagination

Every query over growing stat tables must be filtered and paginated. Defaults:

- `PageSize` default: 25;
- `PageSize` max: 100;
- stable final sort: player display name, then player id, after metric-specific tie-breakers.

Metric ordering:

| Metric | Primary sort | Tie-breakers |
|---|---|---|
| Goals | goals descending | fewer appearances, assists descending, player display name, player id |
| Assists | assists descending | fewer minutes when available, goals descending, player display name, player id |
| Rating | average rating descending | more votes, more appearances, player display name, player id |
| MVP | MVP awards descending | fewer appearances, rating descending, player display name, player id |

The public API returns rank values after ordering. Players tied through all metric-specific
tie-breakers still receive deterministic row order from the stable final sort.

### M9.3 - Optional Projection Store

Do not add Azure Table Storage or another projection store in M9 unless a measured read/scale
problem appears. The first implementation uses Azure SQL read queries over indexed raw tables.

## Read Rules

### Eligible Matches

Leaderboard-visible facts come only from matches that are published/locked/completed according to
the M8 match status contract. Matches in `NeedsReview` are excluded until resolved.

### Goals and Assists

- Count `MatchEventType.Goal` only when the event is approved.
- Own goals are excluded from scorer goals.
- Assists are counted from `AssistPlayerProfileId` on approved goal events.
- A goal with no assist does not create an assist count.
- A player can only receive goal/assist credit for a match where they have a participation row.

### Appearances and Minutes

- Appearances are `PlayerMatchStats.Played == true`.
- Minutes are summed when `MinutesPlayed` is present.
- Missing minutes do not block appearances, goals, assists, MVP, likes, or rating.
- Minutes-based tie-breakers fall back to appearances when minutes are unavailable.

### Ratings, Likes, and MVP

- Average rating is derived from votes received, not votes cast.
- A match with zero received votes does not contribute to the player's average rating.
- Likes count `PlayerLike.ReceiverPlayerProfileId`.
- MVP count comes only from explicit `MatchAwardType.Mvp` awards.

### Recent Form

Profile recent form is derived from `TeamAssignment` joined to `MatchResult` for the assigned
`MatchTeam`.

- Checked-in but unassigned players do not receive a result.
- Multi-team rotations use each team's persisted W/D/L counters.
- Validate at write time and preserve at read time: `wins + draws + losses <= session.TeamCount - 1`.
- Recent form returns newest eligible matches first and is bounded, defaulting to the latest 5.

## Contracts and API

Reuse the existing `SouthBaySoccer.Contracts.Leaderboards` contract and extend only if the backend
needs pagination metadata that the client contract does not yet expose.

Proposed endpoints:

| Endpoint | Policy | Purpose |
|---|---|---|
| `GET /api/v1/stats/leaderboards?seasonId=&metric=&page=&pageSize=` | `AuthenticatedPlayer` | Season leaderboard page |
| `GET /api/v1/players/{playerProfileId}/stats?seasonId=` | `AuthenticatedPlayer` | Public player season/career stats |
| `GET /api/v1/players/me/stats?seasonId=` | `AuthenticatedPlayer` | Current player's profile stats |
| `GET /api/v1/players/{playerProfileId}/recent-form?take=` | `AuthenticatedPlayer` | Recent W/D/L form |

All endpoints must have exactly one endpoint access attribute and return RFC 7807 errors through the
existing Functions pipeline.

## Implementation Order

1. Add query request/response models in Application for leaderboard pages, player stat summary, and
   recent form.
2. Add read-specific methods to the stats repository contract, returning projection read models
   rather than entity graphs.
3. Implement SQL-backed repository queries with filtering, grouping, ordering, pagination, and no
   client-side sorting over unbounded result sets.
4. Add Application query handlers that validate pagination bounds and map read models to contracts.
5. Add Functions endpoints and endpoint metadata tests.
6. Add Application tests for query behavior and tie-breakers.
7. Add Infrastructure tests that seed raw M8 rows and prove approved-event filtering, own-goal
   exclusion, profile merge visibility, recent-form joins, and pagination.
8. Run backend build and relevant test projects.

## Acceptance Criteria

```gherkin
Scenario: Season leaderboard derives from raw match rows
  Given a season has completed matches with approved goals, assists, ratings, likes, and MVP awards
  When the goals leaderboard is requested for that season
  Then the response aggregates rows through Match to Session to Season
  And no maintained totals table is read
  And own goals are not credited to a scorer

Scenario: Unapproved or conflicted facts are hidden
  Given a match event is pending review or the match is in NeedsReview
  When a leaderboard or profile stat query runs
  Then that fact does not affect the returned totals

Scenario: Golden boot ordering is deterministic
  Given two players have equal goals
  When the goals leaderboard is requested
  Then the player with fewer appearances ranks higher
  And if appearances are equal, the player with more assists ranks higher
  And if still tied, display name and player id provide stable order

Scenario: Career stats are derived on read
  Given a player has raw stats across multiple seasons
  When the player stats query runs without a season filter
  Then matches, goals, assists, average rating, likes, and MVP awards are computed from raw rows
  And no mutable profile total is updated

Scenario: Recent form comes from team results
  Given a player is assigned to a match team with a persisted MatchResult
  When recent form is requested
  Then the player's W/D/L result is derived from TeamAssignment and MatchResult
  And checked-in unassigned players do not receive a result

Scenario: Multi-team result counters stay bounded
  Given a session has four teams
  When MatchResult rows are read for a team
  Then wins plus draws plus losses is no greater than three

Scenario: Stat queries are paginated
  Given more than one page of ranked players exists
  When a leaderboard query requests page 2
  Then only that page is returned
  And the page size cannot exceed the configured maximum
```

## Test Matrix

| Layer | Coverage |
|---|---|
| Application.Tests | pagination bounds, metric validation, tie-break ordering, handler maps repository projections |
| Infrastructure.Tests | SQL grouping across Match/Session/Season, approved-event filtering, own-goal exclusion, assists from goal events, rating average, MVP count, recent form |
| Functions.Tests | endpoint policy metadata, route shape, query parameter binding, ProblemDetails for invalid query values |
| Client.Tests | no new M9 requirement unless contracts change; existing LEAD-4 tests remain valid |

## Non-goals

- No Azure Table Storage projection in the first pass.
- No background leaderboard materializer.
- No client UI redesign.
- No payment, RSVP, or waiver behavior changes.
- No rework of M8 stat recording beyond defects discovered while proving M9 queries.
