# TEAM-5 — Ranked Captains, Snake-Order Draft, and Auto-Balanced Teams

Builds on TEAM-4 (captain assignment and manual draft). Adapted from the classic
balanced-pickup-teams recipe: rating-ordered snake draft plus an iterative improvement pass that
minimizes the spread between team rating averages.

## Captain ranking

```gherkin
Scenario: Selection order becomes captain rank
  Given a game admin is assigning 3 captains
  When they tap Desire first, Lord second, and Vic third
  Then Desire is the 1st captain (team 1), Lord the 2nd (team 2), Vic the 3rd (team 3)
  And each selected row shows its rank badge ("1st captain", ...)
  And deselecting a captain re-ranks everyone below them
  And the grant sends the captains in rank order
```

## Snake-order manual draft

```gherkin
Scenario: Captains pick one player at a time in snake order
  Given teams 1..N with ranked captains and server-computed caps (roster / N, remainder to the
    highest ranks)
  Then the pick order is 1..N, then N..1, and so on, skipping full teams
  And the draft page always shows whose turn it is ("On the clock: Team Desire" / "Your turn")
  And a captain tapping a player on their turn drafts them immediately (no Save step)
  And a captain acting off-turn is rejected with the on-the-clock team named
  And a game admin may pick on the on-the-clock captain's behalf
  And the bulk "Save team picks" flow is game-admin only (correction tool)

Scenario: Caps are server policy
  Given 16 eligible players and 3 teams
  Then the server projects caps 6/5/5 (extra to the 1st-ranked team)
  And the client renders the projected caps without recomputing them
```

## Player draft watch (read-only)

```gherkin
Scenario: Rostered players can watch the draft live
  Given a rostered player (not a captain or admin) while the draft is running
  When they open "View your team" from Game Day
  Then they see a "Draft in progress" banner with whose turn it is
  And every team sheet so far, their own team and name marked
  And a "Yet to be picked (N)" list of the Going/Waitlist players still on the board
  And nothing on the page is tappable — watching only
  And once teams lock, the banner and the yet-to-be-picked list disappear
```

## Auto-balance (admin only)

```gherkin
Scenario: Admin deals balanced teams
  Given a game admin on the draft page of a still-Draft match with every captain assigned
  When they tap "Auto-balance teams" and confirm
  Then every team is re-dealt: captains stay on their ranked teams, everyone else is distributed
    by shrunken peer-rating score (snake fill, then best-swap optimization of the average spread)
  And every eligible player appears exactly once, every team hits its projected cap
  And tapping again re-deals differently (a new attempt reseeds the deterministic shuffle)

Scenario: Auto-balance is guarded
  Given a captain (not a game admin)
  Then the auto-balance action is absent and the endpoint rejects them
  Given a match that has left Draft (locked, completed, or under review)
  Then auto-balance is rejected — post-game corrections never re-deal teams

Scenario: Ratings with little history
  Given a player with no peer-rating votes (new player or guest)
  Then their balancing score is the roster's average, never zero
  And a player with few votes is pulled toward the roster average (prior weight ~4 votes)
```
