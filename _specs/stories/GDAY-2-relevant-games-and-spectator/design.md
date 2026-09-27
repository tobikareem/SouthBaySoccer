# GDAY-2 - Design

Anchored to wireframe screens `gameday` (participant + admin scope switch), `gameday-spectator`,
and `gameday-empty`.

## Backend

- `GetTodayGameDayContextQueryHandler` (`Application/Features/Scheduling/GameDayHandlers.cs`)
  - Signature: `HandleAsync(Guid? requestedSessionId, bool showAllGames, ct)`.
  - Pool: all candidates when `showAllGames && IsGameAdmin`; else Going/Waitlisted candidates; else
    the group pool via `ISessionRepository.GetGroupNamesBySessionAsync` ×
    `IPlayerGroupLinkRepository.ListPlayerGroupsAsync` (trim + OrdinalIgnoreCase); else null → 204.
  - Spectator (selected game not attended, not showing all): status projection
    `("Blocked", "Spectator", …)`, every action flag forced false, roster still returned;
    `CanJoin = Published && now < RsvpDeadlineUtc`, else `JoinBlockedReason`.
  - New `GameDayContextModel`/`GameDayContextDto` fields (additive):
    `Title, GroupName, IsSpectator, CanJoin, JoinBlockedReason, Capacity, CanShowAllGames,
    IsShowingAllGames`.
- `GetLastGameSummaryQueryHandler` + `GET game-day/last-game` (`AuthenticatedPlayer`, 200/204):
  30-day lookback via `ISessionRepository.ListPastGameDayCandidatesAsync` (newest first), attended
  first then group-matched; counts from the attendance batch; `ResultSummary`
  ("Team Vic 2W · Team Ade 1W 1D") only for Published/Locked matches. Counts only — no roster PII.
  - **Known limit (deferred):** the candidate page is global (newest 60) and relevance filters in
    memory, so at roughly >5 active groups the 30-day promise erodes. The fix is a SQL-side query
    joining the player's RSVP/check-in rows and group names inside the window — do that instead of
    raising the cap again.

## Client

- `IGameDayClient.GetTodayContextAsync(sessionId, allGames, ct)` + `GetLastGameSummaryAsync(ct)`.
- `GameDayPageModel`: three mode flags (`IsParticipant` / `IsSpectator` / `IsNoGame`),
  `HeaderContextLabel` ("{Group} · {Venue}"), `SpectatorBannerText`, `JoinCommand`
  (via `IRosterClient.SetRsvpIntentAsync`, full reload on success, dialog on failure),
  `SelectGameScope`/`ToggleAllGames` for the admin switch, `LastGame` + `LastGameCountsLabel`.
  Spectator defense-in-depth: every action flag re-zeroed client-side.
- `GameDayPage.xaml`: shared header (state-mapped `Badge` triggers, spectator → Neutral), admin
  `SegmentedControl`, shared picker + StatTiles, spectator `NoticeSurface` banner + Join
  `BrandCard IsTinted` with `CapacityBar`, participant hero (`HeroInverseButton`) + actions,
  no-game `IconTileSurface` headline + Last game `BrandCard`.

## Tests

- Application: filtering matrix + spectator flags in `GameDayContextHandlerTests`;
  `GetLastGameSummaryHandlerTests` for relevance order, window, and result gating.
- Functions: route/policy metadata for `game-day/last-game`.
- Client: mode rendering, Join success/failure/offline, scope toggle, no-game states, and XAML
  structure in `GameDayPageModelTests`.
