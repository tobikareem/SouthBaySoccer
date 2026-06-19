# PROF-5 — Player profile screen

**Epic:** PROF — Player Profiles & Identity · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `INV-7` (stats derived-on-read), `NFR-Accessibility`,
`NFR-Iconography` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the `profile` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* signed-in SouthBaySoccer player, *I want* a profile screen that shows my identity and career
stats, *so that* I can see how I am doing and jump to the season leaderboard — while editing my
account stays on Pickup Pal.

This screen directly implements the `profile` wireframe. In the UI-first phase its identity, career
stats, and recent form load from a seed `IProfileClient` (no backend — see
[`../../design.md`](../../design.md) §12). Profile editing is **not** an in-app screen: the
"Edit on Pickup Pal" action opens the external Pickup Pal account page.

## Acceptance criteria

```gherkin
Scenario: The profile screen matches the wireframe composition
  Given my profile, career stats, and recent form have loaded
  When the profile screen is displayed
  Then a brand header shows my avatar, my name "Tobi Kareem", and the subtitle "\"Captain\" · #8"
  And a "Linked via WhatsApp" success badge and an "Edit on Pickup Pal" link appear below the header
  And a "Career stats" section header precedes a three-column stat-tile grid
  And the stat tiles show Matches 24, Goals 12, Assists 9, Avg rating 7.8, MVP 3, and Likes 41
  And a "Recent form" card shows the last five results as W, W, D, W, L badges
  And a muted note reads "2 goals from Sat awaiting confirmation"
  And a "View season leaderboard" ghost button is the final content
  And the bottom tab bar is shown with Profile active

Scenario: Identity, career stats, and recent form load from the seed profile client
  Given the profile page model requests the current player's profile
  When the seed IProfileClient returns the wireframe fixture
  Then the header binds the player's avatar, name, and captain/number subtitle
  And the stat-tile grid binds the career-stat totals
  And the recent-form card binds the ordered last-five results
  And no value is hard-coded in the page or page model

Scenario: The pending-confirmation note appears only when there is unconfirmed stat activity
  Given the seed profile reports stats awaiting confirmation
  When the profile screen is displayed
  Then the muted pending note is shown with its clock pictogram
  And the note text reflects the pending amount and source
  When the seed profile reports no stats awaiting confirmation
  Then the pending note is not shown

Scenario: Editing the profile happens on Pickup Pal, not in the app
  Given the profile screen is displayed
  When I activate "Edit on Pickup Pal"
  Then the external Pickup Pal account page is opened through the external launcher
  And no in-app profile edit form is presented
  And the player's stats are not modified by this action

Scenario: Viewing the season leaderboard navigates to the leaderboard
  Given the profile screen is displayed
  When I activate "View season leaderboard"
  Then the app navigates to the Leaderboard screen

Scenario: The screen surfaces loading, empty, error, and offline states
  Given the profile data is being requested
  Then the StateView shows the loading state while the request is in flight
  And it shows the empty state when no profile is available
  And it shows a retryable error state when the request fails
  And it shows the offline state when the device has no connectivity
  And retrying re-requests the profile from the client

Scenario: Iconography uses Font Awesome instead of emoji
  Given the profile screen contains WhatsApp, pending-clock, and tab-bar pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the profile screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And the stat-tile grid reflows without overlapping
  And content remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`AUTH-7`](../AUTH-7-welcome-back-screen/requirements.md) — establishes the signed-in session this screen requires and the Font Awesome / external-launch patterns reused here.
- `LEAD-1` — the season leaderboard reached from "View season leaderboard".
- `STAT-3` — self-submitted stats whose pending confirmation is reflected in the muted note.
