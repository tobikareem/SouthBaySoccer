# AUTH-8 - Pickup Pal phone sign-in from Welcome Back

**Epic:** AUTH - **Milestone:** M11 - **Client + backend story**
**Applies:** `NFR-Security`, `INV-11` (fail-closed), `AUTH-3`/`AUTH-4` (token issue + refresh) -
see [`../../requirements.md`](../../requirements.md). **Screen:** [`AUTH-7`](../AUTH-7-welcome-back-screen/requirements.md).

## Story

*As a* returning player, *I want* to sign in with the phone number on my Pickup Pal account, *so that*
SouthBaySoccer can verify my account and issue app tokens without a password.

## Acceptance criteria

```gherkin
Scenario: Valid phone number starts password-free sign-in
  Given the Welcome Back screen is displayed
  And I enter a valid international phone number
  When I select "Sign in with phone"
  Then the client posts the phone number to the SouthBaySoccer phone sign-in endpoint
  And the primary action enters a busy state and cannot be submitted twice
  And no authenticated route opens until SouthBaySoccer returns access and refresh tokens

Scenario: Invalid phone number is rejected locally
  Given the Welcome Back screen is displayed
  When I enter a missing or invalid phone number
  And I select "Sign in with phone"
  Then an inline validation message explains the required phone format
  And no network request is sent

Scenario: Pickup Pal account is not found
  Given a valid phone number is entered
  When Pickup Pal does not have a user for that number
  Then a non-sensitive message asks me to sign up on Pickup Pal
  And no tokens are stored
  And the app remains on the Welcome Back screen

Scenario: Phone sign-in failure is recoverable
  Given a valid phone number is entered
  When the phone sign-in request fails because the service is unavailable or the device is offline
  Then a non-sensitive error message is displayed
  And the number remains available for correction or retry
  And the primary action becomes enabled again

Scenario: Pickup Pal phone match completes sign-in
  Given Pickup Pal has a user for my phone number
  When the Function App syncs the returned Pickup Pal user locally
  Then the Function App issues SouthBaySoccer access and refresh tokens
  And the tokens are stored using platform secure storage
  And the app replaces the Welcome Back route with the authenticated Sessions route
```
