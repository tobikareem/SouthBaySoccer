# RSVP-8 — Session detail screen

**Epic:** RSVP — RSVP & Waitlist · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `INV-12` (RSVP records attendance intent only, not
check-in), `NFR-Accessibility`, `NFR-Iconography` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the `session` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* SouthBaySoccer player, *I want* a session detail screen that shows when and where the game is,
who is going, who is waitlisted, and how full it is, *so that* I can decide and confirm with one tap
whether I'm playing.

This screen directly implements the `session` wireframe. During the UI-first phase its going list,
waitlist, capacity, and the RSVP action are served by a seed `IRosterClient` / `ISessionsClient`
(no backend — see [`design.md`](design.md) and [`../../design.md`](../../design.md) §12 and the
[`SEED-1`](../SEED-1-seed-data-providers/requirements.md) seed providers).

## Acceptance criteria

```gherkin
Scenario: The screen matches the session wireframe hierarchy
  Given a session is opened from the sessions list
  Then a green-to-pine BrandHeader shows a back button, the "Saturday pickup" eyebrow, and the venue title "Marina Field"
  And a When/Where BrandCard shows the date-and-time row and the location row with a "Map" affordance
  And a capacity BrandCard shows a CapacityBar labelled "16 / 20 going" and a Warning Badge "closes 1d 4h"
  And a SectionHeader "Going · 16" precedes the going PlayerRow list
  And a SectionHeader "Waitlist · 3" precedes the ordered waitlist PlayerRow list
  And a full-width PrimaryButton "RSVP — I'm going" and a subtle "Confirmed via Pickup Pal" note are the final content

Scenario: Going list, waitlist, and capacity load from the seed roster client
  Given the Seed provider is the active client implementation
  When the session detail screen loads
  Then the When/Where details and capacity come from the seed ISessionsClient
  And the going roster and waitlist come from the seed IRosterClient
  And no call performs network, file, or database access
  And the going list shows each player's avatar, name, and position
  And the going list shows the current player marked "· you" and a "+ 12 more going" affordance

Scenario: The waitlist shows ordered positions and a guest badge
  Given the waitlist has three entries
  Then each waitlist PlayerRow shows its ordinal position number 1, 2, and 3
  And position 1 is a guest, "Tunde B.", shown with a "guest" Badge and marked "next up"
  And the order reflects the seed waitlist order without reordering it in the page

Scenario: RSVP toggles between "I'm going" and a confirmed state
  Given the session detail screen is displayed and I am not yet going
  When I select "RSVP — I'm going"
  Then the page model records my going intent through the seed roster client optimistically
  And the primary action toggles to its confirmed state
  And the subtle "Confirmed via Pickup Pal" note is shown
  And selecting the confirmed action again toggles me back to "RSVP — I'm going"

Scenario: RSVP records attendance intent only
  Given I confirm "RSVP — I'm going"
  Then the action records attendance intent for the session
  And it does not check me in or record an attendance outcome

Scenario: Canceled session detail is read-only
  Given an admin canceled the session
  When I open its detail screen
  Then a placard says "Session has been cancelled"
  And the date, venue, capacity, and roster remain visible
  And the RSVP action is hidden or disabled

Scenario: Loading, empty, error, and offline are shown through StateView
  Given the session detail screen is loading its roster and capacity
  Then a StateView loading state is shown until the data resolves
  And when the roster client fails a recoverable StateView error with a retry action is shown
  And when the device is offline a StateView offline state is shown
  And when a session has no going players and no waitlist a StateView empty state is shown

Scenario: Back navigation returns to the previous screen
  Given the session detail screen is displayed
  When I select the BrandHeader back button
  Then the page model raises its Back command
  And the app returns to the screen that opened the session

Scenario: Iconography uses Font Awesome instead of emoji
  Given the screen contains back, calendar, map-pin, clock, guest, and connected-via pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font referenced by a semantic name
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the session detail screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And the going and waitlist lists remain vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`SEED-1`](../SEED-1-seed-data-providers/requirements.md) — the seed `ISessionsClient` /
  `IRosterClient` and guest fixture this screen binds to in the UI-first phase.
- [`RSVP-1`](../../requirements.md) / [`RSVP-3`](../../requirements.md) — the eligible-RSVP and
  waitlist rules this screen reflects; their server-side transactional enforcement (INV-2/3) is
  re-verified when the backend milestone lands.
- [`CHK-1`](../../requirements.md) — check-in and attendance outcome, which this screen does **not**
  perform (`INV-12`).
