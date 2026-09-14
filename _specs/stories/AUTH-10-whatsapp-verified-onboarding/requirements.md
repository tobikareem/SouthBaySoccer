# AUTH-10 - WhatsApp-verified sign-up and sign-in verification

**Epic:** AUTH - **Milestone:** M13 - **Client + backend + external (Pickup Pal) story**
**Applies:** `NFR-Security`, `INV-11` (fail-closed), `AUTH-3`/`AUTH-4` (token issue + refresh),
`WAIV-1`/`WAIV-2` (waiver gate after onboarding) - see [`../../requirements.md`](../../requirements.md).
**Screens:** `documentation/mobile-wireframes.html` screens `signin`, `signin-verify`,
`signin-waiting`, `signup-start`, `signup-waiting`, `signup-expired`, `signup-details`,
`signup-welcome`.
**Supersedes:** the "sign up on the web, then come back" path in
[`AUTH-9`](../AUTH-9-pickup-pal-actions/requirements.md). Extends [`AUTH-8`](../AUTH-8-continue-with-whatsapp/requirements.md)
with a possession check.
**External contract:** [`documentation/pickuppal-account-creation-api.md`](../../../documentation/pickuppal-account-creation-api.md).

## Context

Pickup Pal remains the identity source of truth. WhatsApp does not allow the Pickup Pal bot to
start a conversation, so every proof of phone ownership begins with the player sending the bot a
message. N9ja Bay prefills that message, the bot replies with a link that opens N9ja Bay carrying a
short-lived single-use token, and N9ja Bay's Function App redeems the token against Pickup Pal.
The MAUI client never calls the Pickup Pal API directly.

Two commands, one mechanism:

| Command the app prefills | Who sends it | Bot mints | Bot replies with | N9ja Bay then |
|---|---|---|---|---|
| `!!register n9jabay` | a player with no Pickup Pal account | pre-registration token bound to the sender's JID + phone | `https://<app-link-host>/register?token=<uuid>` | collects details and creates the linked account |
| `!!login n9jabay` | a player whose number matched an existing account | login token bound to the sender's JID + existing user | `https://<app-link-host>/login?token=<uuid>` | redeems the token and issues app tokens |

## Story A - sign up from the app

*As a* new player, *I want* to create my account from inside N9ja Bay by confirming my number on
WhatsApp, *so that* I never have to visit the Pickup Pal website and my account is linked to my
WhatsApp identity from the first day.

## Story B - verified sign-in

*As a* returning player, *I want* sign-in to confirm I actually control the phone number I typed,
*so that* nobody can open my account just by knowing my number, and *I want* to skip that step on a
device I have already verified.

## Acceptance criteria

```gherkin
Scenario: Start sign-up from the Welcome Back screen
  Given I am on the Welcome Back screen
  When I select "Create your account"
  Then the sign-up explainer (Step 1 of 3) is displayed
  And it shows the exact message "!!register n9jabay" and the Pickup Pal bot number from typed configuration
  And no network request has been made

Scenario: Hand off to WhatsApp with the register command prefilled
  Given the sign-up explainer is displayed
  When I select "Continue with WhatsApp"
  Then the app opens WhatsApp with "!!register n9jabay" prefilled to the configured bot number
  And the app shows the waiting screen (Step 2 of 3) with a 15-minute countdown
  And returning from WhatsApp without a link does not authenticate or create anything

Scenario: Bot link opens the app with a registration token
  Given the waiting screen is displayed
  When the app is opened by the configured app link with a registration token
  Then the client posts the token to the Function App registration-validate endpoint
  And when the token is valid the details form (Step 3 of 3) is displayed with the verified number shown read-only
  And the token is never logged, never placed in analytics, and never stored beyond the current flow

Scenario: Registration token is expired or already used
  Given the app was opened with a registration token
  When the Function App reports the token as expired or invalid
  Then the expired screen is displayed and states that nothing was created
  And "Send the message again" returns to the WhatsApp handoff
  And if the bot reported an existing account, the screen points to sign-in instead

Scenario: Email is already registered
  Given the details form is displayed
  When I enter an email that Pickup Pal already has an account for
  Then the form shows an inline error before submitting and offers "Sign in instead"
  And the registration token is not spent

Scenario: Create the linked account
  Given the details form is complete with first name, last name, available email, password of at least 6 characters, and accepted terms
  When I select "Create account"
  Then the Function App creates the Pickup Pal user with the token so the account is linked to my WhatsApp identity
  And the Function App syncs the local identity and PlayerProfile exactly as phone sign-in does
  And the Function App issues N9ja Bay access and refresh tokens
  And the welcome screen is displayed
  And the welcome screen shows group membership only after the Function App re-reads it from Pickup Pal

Scenario: Welcome screen leads to the waiver
  Given the welcome screen is displayed
  When I continue
  Then the waiver and code of conduct step (WAIV-1) is presented before any RSVP is possible

Scenario: Sign-in requires possession on a new device
  Given I am on the Welcome Back screen on a device with no valid refresh token
  And I enter a phone number that matches a Pickup Pal account
  When I select "Sign in with phone"
  Then the verify screen shows the matched account's masked number and "!!login n9jabay"
  And no N9ja Bay tokens are issued yet

Scenario: Login link completes sign-in
  Given the sign-in waiting screen is displayed
  When the app is opened by the configured app link with a login token
  Then the Function App redeems the token with Pickup Pal and confirms it belongs to the matched user
  And the Function App issues N9ja Bay access and refresh tokens
  And the app replaces the Welcome Back route with the authenticated Sessions route

Scenario: Login token mismatch is rejected
  Given the sign-in waiting screen is displayed for user A
  When the app receives a login token that Pickup Pal resolves to a different user
  Then no tokens are issued
  And a non-sensitive error returns me to the Welcome Back screen

Scenario: Remembered device skips verification
  Given I completed a verified sign-in on this device with "Remember this device" on
  And my refresh token is still valid
  When I open the app
  Then I am signed in without a WhatsApp round-trip
  And signing out revokes the refresh token so the next sign-in requires verification again

Scenario: Account deletion is available in-app
  Given I am signed in
  When I choose to delete my account from Profile and confirm
  Then the Function App soft-deletes my local records and calls the Pickup Pal deletion endpoint
  And my session tokens are revoked
  And the app returns to the Welcome Back screen
```

## Out of scope

- Email verification. Pickup Pal has none; the email stays unverified and is never used as a
  security factor.
- Group linking UI. Auto-join is best-effort on the Pickup Pal side; manual linking stays in the
  existing group link story.
- Web fallback. Players who cannot use WhatsApp continue to sign up on the Pickup Pal website and
  then sign in by phone.
