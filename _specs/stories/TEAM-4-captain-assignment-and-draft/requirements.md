# TEAM-4 - Captain assignment and team draft

**Epic:** TEAM - Teams & Matches / ADMIN - Live game  
**Milestone:** M11 client first, then M8 backend  
**Visual source:** the `captains` and `draft` screens in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## User stories

*As a* GameAdmin, *I want* to choose 2, 3, or 4 captains from the checked-in game-day roster, *so that*
team selection is delegated to trusted captains.

*As a* captain for a session, *I want* to select players into my team from the confirmed game list,
*so that* teams can be formed at the field before play starts.

## Acceptance criteria

```gherkin
Scenario: GameAdmin assigns two, three, or four captains
  Given I have CanAssignTeams
  And the session has checked-in players
  When I choose "2 captains", "3 captains", or "4 captains"
  And select that many captains from the checked-in roster
  Then each selected captain receives a session-scoped TeamDraft.PickPlayer permission
  And a two-captain setup creates two teams
  And a three-captain setup creates three teams
  And a four-captain setup creates four teams
  And the assignment is audited with actor, session, captain count, captains, and timestamp

Scenario: Captains are selected only from checked-in players
  Given checked-in players exist for the session
  When a GameAdmin opens captain assignment
  Then only checked-in players are selectable as captains
  And a name search filters the checked-in player list
  And Going-but-not-checked-in players are not selectable as captains

Scenario: Captain selection is capped by the selected team count
  Given a GameAdmin is assigning captains
  When the GameAdmin chooses "2 captains"
  Then no more than 2 player checkboxes can be selected
  When the GameAdmin chooses "3 captains"
  Then no more than 3 player checkboxes can be selected
  When the GameAdmin chooses "4 captains"
  Then no more than 4 player checkboxes can be selected

Scenario: Only assigned captains can draft players
  Given I am not an assigned captain for this session
  When I attempt to open the captain draft screen
  Then access is denied server-side
  And the client does not show draft controls as an authority

Scenario: Captain picks unassigned checked-in players only
  Given I am an assigned captain with TeamDraft.PickPlayer for this session
  When I search and select players from the checked-in game list
  Then each selected player is assigned to my MatchTeam
  And a player already assigned to another team cannot be selected again
  And roster counts update immediately

Scenario: Team draft locks before result recording
  Given captains have completed team selection
  When a GameAdmin locks teams
  Then no further captain picks are accepted
  And the final TeamAssignment list is the roster used for stats, ratings, and results
```

## Notes

- Captains must be selected from checked-in players because absent captains break game-day flow.
- Three captains means three separate teams; four captains means four separate teams, not co-captains.
- Permissions are resource-scoped to the session or match and expire when teams are locked.
- Team assignment is per match/session only and never becomes a permanent team on a profile (`INV-9`).
