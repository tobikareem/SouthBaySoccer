# AUTH-9 — Pickup Pal help and signup actions

**Epic:** AUTH · **Milestone:** M11 · **Client story**
**Applies:** `NFR-Security`, `NFR-Accessibility` — see [`../../requirements.md`](../../requirements.md).
**Screen:** [`AUTH-7`](../AUTH-7-welcome-back-screen/requirements.md).

## Story

*As a* player on the Welcome Back screen, *I want* to open the Pickup Pal bot or sign up on the web,
*so that* I can get help or create an account before signing in.

## Acceptance criteria

```gherkin
Scenario: Open the configured Pickup Pal bot
  Given the Welcome Back screen displays the Pickup Pal bot card
  When I select "Open"
  Then the app opens the configured Pickup Pal WhatsApp conversation
  And the bot number is loaded from typed configuration rather than duplicated page text

Scenario: Sign up on Pickup Pal
  Given I am not registered with Pickup Pal
  When I select "Sign up on Pickup Pal"
  Then the app opens the configured HTTPS signup page in the system browser
  And the app does not treat returning from the browser as authenticated

Scenario: External application cannot be opened
  Given WhatsApp or a browser cannot handle the configured URI
  When I select an external action
  Then the app displays a recoverable explanation
  And remains on the Welcome Back screen
```
