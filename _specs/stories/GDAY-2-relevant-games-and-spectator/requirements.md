# GDAY-2 - Relevant games, spectator view, and the no-game state

**Epic:** CHK - Check-in / SES - Sessions
**Visual source:** the `gameday`, `gameday-spectator`, and `gameday-empty` screens in
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
**Supersedes:** the GDAY-1-era behavior where a player with no RSVP fell back to seeing every game
running today.

## User story

*As a* player, *I want* Game Day to show only the game I'm attending — or, failing that, a read-only
view of my WhatsApp group's game — *so that* the page is about my day, not everyone's, and needs no
training to understand.

## Behavior

Game selection is server-side (`GET game-day/today`), in strict priority order:

1. **My games** — today's games I RSVP'd **Going or Waitlisted** to. More than one → the picker.
2. **My group's games (spectator)** — with no RSVP, today's games whose Pickup Pal snapshot
   `GroupName` matches (trim + case-insensitive) a WhatsApp group I'm linked to
   (`PlayerGroupLink`). Rendered read-only with one Join CTA.
3. **Nothing** — every other game is completely hidden; the endpoint returns 204 and the client
   shows the no-game state.

Game admins get the same filtered default plus an explicit **"All games today"** scope switch
(`?all=true`, honoured only for game admins) for running check-in/matching on any game.

## Acceptance criteria

```gherkin
Scenario: A player sees only the game they hold a spot on
  Given two games run today and I am Going to one of them
  When I open Game Day
  Then I see my game with the full check-in view
  And the other game appears nowhere on the page

Scenario: A waitlisted game counts as mine
  Given my only spot today is a waitlist spot
  When I open Game Day
  Then that game loads as a participant view, not a spectator view

Scenario: A group member without a spot spectates
  Given my WhatsApp group "Bay Area Soccer" runs a game today and I have no RSVP
  When I open Game Day
  Then a banner explains I'm a member of Bay Area Soccer and not on this game's list
  And I can open the Going, Waitlist, and Checked-in lists read-only
  And the only action offered is "Join this game" with a capacity bar
  And check-in, captains, drafting, stats, and admin actions are absent

Scenario: Join submits a normal RSVP
  Given I'm spectating and RSVP is still open
  When I tap "Join this game"
  Then the standard RSVP command runs (waiver and payment gates included)
  And the server decides Going versus Waitlisted
  And the page reloads into my participant view

Scenario: Join is closed
  Given I'm spectating and the RSVP deadline has passed
  Then the Join button is replaced by "RSVP is closed for this game."

Scenario: Another group's game stays hidden
  Given a game today belongs to a group I'm not a member of and I have no RSVP
  When I open Game Day
  Then the page shows the no-game state

Scenario: Admins opt into everything
  Given I'm a game admin with no RSVP today
  When I open Game Day
  Then I see my filtered view (or the no-game state) by default
  And switching the scope to "All games today" lists every game with full admin controls

Scenario: No game today shows the last game
  Given nothing relevant runs today
  When I open Game Day
  Then "No game today" is the headline with a plain-language explanation
  And my most recent game (within 30 days, mine or my group's) shows title, group, venue, date,
    going/waitlist/checked-in counts, and the team result once published
  And with no such game at all, only the empty-state explanation shows

Scenario: The last game's teams are browsable down to the scorers
  Given my last game was drafted into teams
  When I view the last-game summary
  Then each team card shows the team name, its captain, and its settled result
  And tapping a team opens its member list with each player's approved goal tally
  And pending or rejected goal submissions are not counted

Scenario: Admins and captains can finish up the last game
  Given I am a game admin or a captain on the last game
  When I view the last-game summary
  Then I see "Lock the teams" when the teams were never locked (admin, within the 3-day edit window)
  And "Match players" when unlinked imported names remain (admin)
  And "Confirm result and goals" when the locked match still awaits confirmation (captain or admin,
    within the post-game window)
  And each action opens the existing screen for that job, scoped to the last game's session
  And a regular player sees none of these actions
```

## Non-goals

- Tapping the last-game card (no player-facing past-session detail screen exists yet).
- Persisting the admin scope switch across loads.
- Auto-linking a session to a group by anything other than the snapshot group name (group ids are
  deliberately never stored).
