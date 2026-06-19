# STAT-8 — Rate teammates screen

**Epic:** STAT — Stats · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `INV-8`/`STAT-3` (peer rating rules: no self-vote,
one vote per voter per rated player per match, integer score 0–10), `STAT-4` (one like per peer per
match), `STAT-5` (single explicit MVP award), `NFR-Accessibility`, `NFR-Iconography` — see
[`../../requirements.md`](../../requirements.md).
**Visual source:** the `rate` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
**Phase:** UI-first — teammates come from a seed `IStatsClient`; the backend is deferred (see [`../../design.md`](../../design.md) §12).

## Story

*As a* SouthBaySoccer player who just played a match, *I want* to rate my teammates 0–10, like a
great game, and pick one MVP, *so that* peer ratings, likes, and the MVP award are captured for the
match.

This screen directly implements the `rate` wireframe. The rater is excluded from the list — you
cannot rate yourself (`INV-8`). In this phase the teammates and the rating submission are served by a
seed `IStatsClient`.

## Acceptance criteria

```gherkin
Scenario: The screen matches the rate wireframe hierarchy
  Given I have rated the match for a session
  When the Rate teammates screen is displayed
  Then a BrandHeader shows a back affordance, the title "Rate the match", and the subtitle "Sat · Marina Field"
  And intro copy displays "Rate teammates 0–10, like a great game, and pick one MVP. You can't rate yourself."
  And one BrandCard is shown per teammate
  And each teammate card shows an Avatar, the teammate name, and a sub-detail
  And each teammate card shows shared IconToggleButton-styled like and MVP actions
  And each teammate card shows a shared RatingSlider-styled 0–10 slider with a value readout
  And a PrimaryButton labelled "Submit ratings" is the final content

Scenario: Teammates load from the seed stats client, excluding the rater
  Given the Rate teammates screen requests the match's rateable teammates
  When the list is loaded from the IStatsClient for the current match and rater
  Then the page model exposes one row per teammate returned by the client
  And the signed-in rater does not appear in the list
  And no card offers a way to rate, like, or MVP the rater (no self-vote, INV-8)

Scenario: Each teammate has an independent 0–10 rating
  Given the Rate teammates screen is displayed
  When I move a teammate's rating slider
  Then that teammate's rating is an integer constrained to the inclusive range 0 to 10
  And the value readout reflects the selected integer
  And changing one teammate's rating does not change any other teammate's rating

Scenario: Like is an independent per-teammate toggle
  Given the Rate teammates screen is displayed
  When I toggle the like control on a teammate card
  Then that teammate is liked at most once for this match (STAT-4)
  And toggling it again clears the like for that teammate
  And each teammate's like is independent of every other teammate's like

Scenario: MVP is single-select across all teammates
  Given the Rate teammates screen is displayed
  And one teammate is currently selected as MVP
  When I select MVP on a different teammate
  Then the previously selected teammate is no longer MVP
  And exactly one teammate is marked MVP across the whole list (STAT-5)
  And selecting MVP again on the marked teammate clears the MVP selection

Scenario: Submitting sends ratings, likes, and the MVP through the seed stats client
  Given I have set ratings, optionally liked teammates, and picked one MVP
  When I select "Submit ratings"
  Then the page model sends each teammate's 0–10 rating, like state, and the single MVP selection to the IStatsClient for the current match
  And the primary action enters a busy state and cannot be submitted twice
  And the rater is never included in the submitted ratings, likes, or MVP

Scenario: Loading, empty, error, and offline states use StateView
  Given the Rate teammates screen requests its teammates
  When the request is in flight
  Then a loading state is shown
  When the match has no rateable teammates
  Then an empty state is shown
  When the request fails or the device is offline
  Then a recoverable error or offline state is shown with a retry action
  And retry re-requests the teammate list

Scenario: Back navigation leaves the screen
  Given the Rate teammates screen is displayed
  When I select the header back affordance
  Then the app returns to the previous screen

Scenario: Iconography uses Font Awesome instead of emoji
  Given the Rate teammates screen shows the back, like (heart), and MVP (star) pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Rate teammates screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And the teammate list remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`STAT-3`](../../requirements.md#epic-stat--stats) — peer rating votes (0–10, no self-vote, one vote per peer per match) realized by this screen.
- [`STAT-4`](../../requirements.md#epic-stat--stats) — likes (one per rater per match) surfaced as the heart toggle.
- [`STAT-5`](../../requirements.md#epic-stat--stats) — the single explicit MVP award surfaced as the star single-select.
