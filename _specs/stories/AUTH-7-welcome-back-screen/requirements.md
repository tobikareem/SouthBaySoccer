# AUTH-7 — Welcome Back screen

**Epic:** AUTH — Authentication & Identity · **Milestone:** M11 · **Client story**
**Applies:** `INV-13` (Font Awesome, no emoji), `INV-11` (fail-closed), `NFR-Security`,
`NFR-Accessibility`, `NFR-Iconography` — see [`../../requirements.md`](../../requirements.md).
**Visual source:** the first `signin` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).

## Story

*As a* returning SouthBaySoccer player, *I want* a clear Pickup Pal phone sign-in screen, *so that* I can
connect the app to my Pickup Pal account without entering a password.

This is the first application screen and directly implements the first `signin` wireframe screen.

## Acceptance criteria

```gherkin
Scenario: Signed-out launch displays the Welcome Back screen
  Given I do not have a valid local authenticated session
  When the MAUI application starts
  Then the Welcome Back screen is the initial route
  And the Shell flyout and authenticated tab navigation are not visible
  And the screen displays the N9ja Bay football mark
  And the header displays "N9ja Bay"
  And the header subtitle displays "Pickup soccer, organized."
  And the content displays "WELCOME BACK"
  And the primary heading displays "Your next game starts here."

Scenario: The screen matches the first mobile wireframe hierarchy
  Given the Welcome Back screen is displayed
  Then it has a Flag Green-to-Pine header with the white flag stripe and decorative motif
  And the content is a white-dominant scrollable surface with 16 device-independent-pixel side padding
  And the phone number field appears before the primary action
  And the security notice appears after the primary action
  And the Pickup Pal bot card appears before the "not on pickup pal?" divider
  And the external signup action and explanatory copy are the final content

Scenario: Iconography uses Font Awesome instead of emoji
  Given the Welcome Back screen contains football, WhatsApp, shield, and external-link pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Welcome Back screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And content remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

## Related stories

- [`AUTH-8`](../AUTH-8-continue-with-whatsapp/requirements.md) — the Pickup Pal phone sign-in action and token exchange.
- [`AUTH-9`](../AUTH-9-pickup-pal-actions/requirements.md) — the bot / signup external actions on this screen.


