# SES-6 — Sessions (home) screen

**Epic:** SES — Seasons, Venues & Sessions · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `NFR-Accessibility`, `NFR-Iconography` —
see [`../../requirements.md`](../../requirements.md). UI-first delivery against seed data follows
[`../../design.md`](../../design.md) §12.
**Visual source:** the `home` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* signed-in SouthBaySoccer player, *I want* a home screen that greets me, shows my dues status,
and lists my upcoming sessions, *so that* I can see what's next and jump straight into a game day or
my stats.

This is the first authenticated screen behind the Shell tab bar and directly implements the `home`
wireframe screen. In this UI-first phase its data is supplied by a seed `ISessionsClient`; no backend
exists yet (design §12).

## Acceptance criteria

```gherkin
Scenario: The home screen matches the wireframe composition
  Given I am signed in and the Sessions home screen is displayed
  Then a greeting header shows "Saturday crew", "Good morning, Tobi", a "Paid" status Badge, and a notifications bell
  And a green tappable hero BrandCard shows the next Marina Field match and my "You're going" status
  And a separate "Submit your latest stats" BrandCard opens the latest match stats
  And a "Coming up" SectionHeader with "See schedule" precedes the remaining session list
  And each remaining session card shows a title, a status Badge, a date row, a CapacityBar, and an action
  And a full session card shows a "Full" Badge and a "Join waitlist" action
  And a bottom tab bar shows Sessions (active), Stats, and Profile

Scenario: The session list and dues status load from the seed client
  Given the Sessions home screen page model resolves a seed ISessionsClient (no backend, design §12)
  When the page appears
  Then the upcoming sessions and my dues status are loaded through the client interface
  And the page model holds no knowledge of whether the data is seeded or from the API

Scenario: Loading, empty, error, and offline states use StateView
  Given the Sessions home screen is loading its data
  Then a StateView Loading state is shown while the request is in flight
  And when no upcoming sessions are returned the stats entry point remains available
  And when the request fails a StateView Error state with a retry affordance is shown
  And when the device is offline a StateView Offline state is shown
  And the Refresh command re-runs the request from any non-content state

Scenario: Tapping a session card opens the Session detail screen
  Given upcoming session cards are displayed
  When I tap a session card
  Then the OpenSession command navigates to the Session detail screen for that session

Scenario: Tapping the stats prompt opens the Match stats screen
  Given the "Submit your latest stats" card is displayed
  When I tap it
  Then the OpenMatchStats command navigates to the Match stats screen

Scenario: Stats prompt remains available without a latest-match projection
  Given the API dashboard does not provide a latest match id
  When the Sessions home screen is displayed
  Then the "Submit your latest stats" card remains visible
  And tapping it opens the Stats tab

Scenario: A full session offers joining the waitlist
  Given a session card shows a "Full" Badge and remaining waitlist count
  When I activate its "Join waitlist" action
  Then the JoinWaitlist command is invoked for that session

Scenario: A canceled session remains visible
  Given an upcoming session has been canceled by an admin
  When the Sessions feed loads
  Then the session remains in its scheduled position
  And its card shows a "Session has been cancelled" placard
  And RSVP and waitlist actions are unavailable

Scenario: A deleted session is absent
  Given an admin soft-deleted a session
  When the Sessions feed loads
  Then that session is not returned or displayed

Scenario: Iconography uses Font Awesome instead of emoji
  Given the home screen contains dues, bell, stats, calendar, capacity, and tab pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font via a typed glyph constant
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Sessions home screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And the session list remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
  And the bottom tab bar remains reachable
```

## Related stories

- [`SES-3`](../../requirements.md) — recurring session generation that produces the upcoming sessions listed here (backend, deferred).
- Session detail and Match stats screens are the navigation targets of this screen's commands.
