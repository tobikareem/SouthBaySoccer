# NAV-1 — Authenticated Shell & bottom tabs

**Epic:** NAV — Navigation & Shell · **Milestone:** M11 · **Client story**
**Applies:** `INV-11` (client-side hiding is UX only; the server re-authorizes every operation),
`INV-13` (Font Awesome, no emoji), `NFR-Accessibility` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the bottom tab bar (Sessions / Stats / Profile) shown on the `home`, `leaderboard`,
and `profile` screens in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* signed-in player, *I want* the app to drop me into a tabbed home after sign-in, *so that* I
can move between **Sessions**, **Stats**, and **Profile**.

This is the navigation spine for the authenticated app. It owns the authenticated Shell, the bottom
tab bar, and the sign-in → Shell transition. The tab destinations are delivered by their own stories
(`SES-6` Sessions, `LEAD-4` Stats/Leaderboard, `PROF-5` Profile); until a destination exists it shows
a `StateView` empty/coming-soon surface.

## Acceptance criteria

```gherkin
Scenario: Successful sign-in enters the authenticated shell
  Given a verified sign-in (the seed WhatsApp challenge) completes
  When the authentication navigator routes forward
  Then the Welcome Back route is replaced by the authenticated Shell
  And a bottom tab bar shows Sessions, Stats, and Profile
  And Sessions is the initial tab

Scenario: Restored session bypasses sign-in at startup
  Given a valid session can be restored from secure storage at startup
  When the app launches
  Then the authenticated Shell is shown directly
  And the Welcome Back screen is not shown

Scenario: Tabs switch root sections without losing the shell
  Given the authenticated Shell is shown
  When I select Stats or Profile
  Then the corresponding root page is shown with that tab marked active
  And the bottom tab bar remains visible

Scenario: Sign-in is unreachable from inside the shell
  Given I am in the authenticated Shell
  When I use the system back gesture from a root tab
  Then I do not return to the Welcome Back screen

Scenario: A tab whose screen is not yet built shows a placeholder
  Given a tab destination story is not yet implemented
  When I open that tab
  Then a StateView shows an empty/coming-soon state
  And the app does not crash or show a blank page

Scenario: Shell uses the brand system and accessible tab icons
  Given the authenticated Shell is shown
  Then the tab bar, header, and surfaces use BrandStyles/BrandColors with no page-local hex
  And each tab icon is a Font Awesome glyph with a semantic name (INV-13)
  And each tab is an interactive target of at least 44 device-independent pixels
```

## Related stories

- [`AUTH-8`](../AUTH-8-continue-with-whatsapp/requirements.md) — its verified challenge calls the
  authentication navigator that NAV-1 routes into the Shell.
- `SES-6` (Sessions tab), `LEAD-4` (Stats tab), `PROF-5` (Profile tab) — the tab destinations.
