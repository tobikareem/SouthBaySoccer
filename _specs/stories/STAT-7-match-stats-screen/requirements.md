# STAT-7 — Match stats screen (self-submit + confirm)

**Epic:** STAT — Stats · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `STAT-1` (goals/assists as match events), `STAT-2`
(one stats row per participant), `STAT-6` (stats lock + audited correction),
`NFR-Accessibility`, `NFR-Iconography` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the `matchstats` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* SouthBaySoccer player who just finished a match, *I want* to enter my own goals and assists
and submit them for review, *so that* a captain or game admin can confirm them before they count
toward the leaderboard.

This screen implements the `matchstats` wireframe: the player self-submits final totals, the
submission flips to a pending state, and a captain or admin confirms each teammate's submission. It
is the UI-first surface for the simple self-submit model; data is served by a seed `IStatsClient`
with no backend (see [`design.md`](design.md) and [`../../design.md`](../../design.md) §12).

## Acceptance criteria

```gherkin
Scenario: The screen matches the matchstats wireframe hierarchy
  Given the Match stats screen is displayed for a finished match
  Then it has a Green-to-Pine BrandHeader with a back button
  And the header title displays "Match stats"
  And the header subtitle displays the match context "Sat · Marina Field"
  And an informational notice explains that a captain or game admin confirms every submission
  And a "Your performance" SectionHeader precedes a BrandCard
  And the BrandCard contains a Goals CounterStepper row and an Assists CounterStepper row
  And a full-width "Submit for confirmation" PrimaryButton follows the card
  And a note reads "Sent to Pickup Pal · pending captain/admin"
  And a "Confirm teammates · captain" SectionHeader precedes the teammate confirmation list
  And a "Rate teammates instead" link is the final content

Scenario: The Goals and Assists steppers adjust the player's totals
  Given the Match stats screen is displayed
  When I tap the increment control on the Goals row
  Then the Goals value increases by one
  When I tap the decrement control on the Goals row
  Then the Goals value decreases by one
  And the same behavior applies to the Assists row

Scenario: A stepper does not go below zero
  Given the Goals value is zero
  When I tap the decrement control on the Goals row
  Then the Goals value remains zero
  And the decrement control is disabled at the lower boundary

Scenario: Submitting for confirmation flips to a pending state
  Given I have entered my goals and assists
  When I tap "Submit for confirmation"
  Then the submission is sent through the seed IStatsClient
  And the submit state becomes pending
  And the primary button reflects the pending state and is disabled against resubmission
  And the steppers no longer accept further edits while pending

Scenario: A captain confirms a teammate's submission
  Given the teammate confirmation list shows teammates with submitted totals
  And one teammate already shows a confirmed indicator
  When I tap "Confirm" on a teammate's row
  Then that teammate's submission is marked confirmed through the seed IStatsClient
  And the row replaces its "Confirm" action with a confirmed indicator
  And the change is reflected optimistically without leaving the screen

Scenario: Data loads from a seed IStatsClient
  Given the backend is deferred in the UI-first phase
  When the Match stats screen loads
  Then the player's current totals and the teammate submissions come from a seed IStatsClient
  And the page model does not depend on the typed API client

Scenario: Loading, empty, error, and offline are shown through StateView
  Given the Match stats screen is loading its data
  Then a StateView loading state is shown
  When the seed client returns no teammate submissions
  Then a StateView empty state is shown for the confirmation list
  When the seed client reports a recoverable error
  Then a StateView error state with a retry action is shown
  When the device is offline
  Then a StateView offline state is shown

Scenario: Navigating to rate teammates
  Given the Match stats screen is displayed
  When I tap "Rate teammates instead"
  Then the app navigates to the Rate teammates screen for the same match

Scenario: Back navigation leaves the screen
  Given the Match stats screen is displayed
  When I tap the header back button
  Then the app returns to the previous screen

Scenario: Iconography uses Font Awesome instead of emoji
  Given the Match stats screen contains goals, assists, info, confirm, connection, and chevron pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font referenced by a semantic name
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Match stats screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And content remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`STAT-1`](../../requirements.md) — goals and assists as recorded match events (the data this screen submits).
- [`STAT-2`](../../requirements.md) — one `PlayerMatchStats` row per participant (the row a submission populates).
- [`STAT-6`](../../requirements.md) — stat lock and audited correction once confirmed submissions are published.
- **Rate teammates** — the ratings / likes / MVP screen reached by "Rate teammates instead" (`rate` wireframe).
