# AUTH-8 — Continue with WhatsApp from Welcome Back

**Epic:** AUTH · **Milestone:** M11 · **Client + backend story**
**Applies:** `NFR-Security`, `INV-11` (fail-closed), `AUTH-3`/`AUTH-4` (token issue + refresh) —
see [`../../requirements.md`](../../requirements.md). **Screen:** [`AUTH-7`](../AUTH-7-welcome-back-screen/requirements.md).

## Story

*As a* returning player, *I want* to request a one-time WhatsApp sign-in link, *so that* I can
authenticate through the Pickup Pal account connected to my phone number.

## Acceptance criteria

```gherkin
Scenario: Valid WhatsApp number starts password-free sign-in
  Given the Welcome Back screen is displayed
  And I enter a valid international phone number
  When I select "Continue with WhatsApp"
  Then the client requests a one-time Pickup Pal sign-in challenge
  And the primary action enters a busy state and cannot be submitted twice
  And no authenticated route opens until the challenge is verified

Scenario: Invalid WhatsApp number is rejected locally
  Given the Welcome Back screen is displayed
  When I enter a missing or invalid phone number
  And I select "Continue with WhatsApp"
  Then an inline validation message explains the required phone format
  And no network request is sent

Scenario: Challenge request failure is recoverable
  Given a valid phone number is entered
  When the challenge request fails because the service is unavailable or the device is offline
  Then a non-sensitive error message is displayed
  And the number remains available for correction or retry
  And the primary action becomes enabled again

Scenario: Verified one-time link completes sign-in
  Given a one-time sign-in challenge was requested for my number
  When the app receives and verifies the Pickup Pal deep link
  Then the Function App exchanges the verified challenge for SouthBaySoccer access and refresh tokens
  And the tokens are stored using platform secure storage
  And the app replaces the Welcome Back route with the authenticated Sessions route
```
