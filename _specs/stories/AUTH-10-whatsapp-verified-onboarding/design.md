# AUTH-10 - WhatsApp-verified onboarding - Design

Realizes [`requirements.md`](requirements.md). Token issue/refresh mechanics are in
[`../../design.md`](../../design.md) section 6. Pickup Pal contract:
[`documentation/pickuppal-account-creation-api.md`](../../../documentation/pickuppal-account-creation-api.md).
Distilled rules: `.ai/memory/pickuppal-account-creation.md`.

## External prerequisites (Pickup Pal bot)

These must exist before the backend slice can be integration-tested. Until they land, the Function
endpoints are built against a fake `IPickupPalOnboardingClient`.

1. `!!register <client>` and `!!login <client>` accept an optional client argument. With
   `n9jabay`, the reply link is `https://<app-link-host>/register?token=...` or `/login?token=...`
   instead of the web URL. Without an argument, behaviour is unchanged.
2. `!!login` mints a single-use, 15-minute token bound to the sender's resolved JID and existing
   user id. It rejects senders with no account ("run !!register").
3. A server-to-server redemption for login tokens, for example
   `POST /api/users/login/whatsapp` with `{ token }` returning the user (password and memberCode
   stripped), 400 on invalid/expired.
4. An API key or bearer token on the bot API that the Function App presents; the create, login,
   lookup, and password-reset routes reject anonymous callers.
5. An account deletion or anonymization endpoint, `DELETE /api/users/:id` or equivalent, that also
   removes `WhatsAppIdentity` rows.

Registration reuses the existing `GET /api/users/register/whatsapp/validate` and
`POST /api/users/register/whatsapp` unchanged.

## Flows

### Sign-up

```text
WelcomeBackPageModel.StartSignUpCommand
  -> navigate signup-start (no network)
SignUpStartPageModel.ContinueWithWhatsAppCommand
  -> IExternalLauncher.OpenWhatsAppAsync(botNumber, "!!register n9jabay")
  -> navigate signup-waiting; start local 15:00 countdown
App link https://<host>/register?token=T  (iOS universal link / Android app link)
  -> IAppLinkRouter routes to SignUpDetailsPageModel with T held in memory only
  -> IOnboardingClient.ValidateRegistrationAsync(T)
       Function App: POST auth/pickuppal/register/validate  -> Pickup Pal validate
       200 { phoneMasked }            -> show details form
       410 problem(expired|invalid)   -> navigate signup-expired
SignUpDetailsPageModel.CreateAccountCommand
  -> presentation validation (names, email regex, password >= 6, terms on)
  -> IOnboardingClient.CheckEmailAsync(email)   (Function App -> GET /api/users/email/:email)
       taken -> inline error, token untouched
  -> IOnboardingClient.RegisterAsync(T, firstName, lastName, email, password, termsVersion, termsAcceptedAt)
       Function App: POST auth/pickuppal/register
         -> Pickup Pal POST /api/users/register/whatsapp
         -> IPickupPalUserSyncService (same as phone sign-in; claims imported profile by phone hash)
         -> re-read GET /api/users/:id/groups for the welcome summary
         -> IAuthenticationTokenIssuer -> access + refresh tokens
  -> IAuthenticationCoordinator.CompleteSignInAsync(tokens) with welcome payload
  -> navigate signup-welcome -> WAIV-1 -> Sessions Shell
```

### Sign-in with verification

```text
WelcomeBackPageModel.SignInCommand
  -> IAuthenticationClient.SignInByPhoneAsync(phone)
       Function App: POST auth/pickuppal/phone/sign-in  (existing)
         now returns 202 { verificationRequired: true, phoneMasked, displayName } when
         the device presents no valid refresh token; tokens are NOT issued here any more
  -> navigate signin-verify
SignInVerifyPageModel.ContinueWithWhatsAppCommand
  -> IExternalLauncher.OpenWhatsAppAsync(botNumber, "!!login n9jabay")
  -> navigate signin-waiting; countdown
App link https://<host>/login?token=T
  -> IAuthenticationClient.CompleteLoginAsync(T, rememberDevice)
       Function App: POST auth/pickuppal/login/complete
         -> Pickup Pal login-token redemption -> user
         -> must match the PickupPalUserId from the pending sign-in (server-side pending record, 15 min)
         -> sync, issue tokens; refresh-token lifetime = 30 days when rememberDevice, else session default
  -> CompleteSignInAsync -> Sessions Shell
```

The existing `auth/whatsapp/challenges` and `auth/whatsapp/challenges/verify` endpoints and the
`IWhatsAppChallengeService` abstraction are legacy from the deferred challenge design. Replace them
with the endpoints above rather than adding a third mechanism; remove the legacy names in the same
milestone.

## Components

**Client (MAUI)**

- Pages and page models: `SignUpStartPage`, `SignUpWaitingPage`, `SignUpExpiredPage`,
  `SignUpDetailsPage`, `SignUpWelcomePage`, `SignInVerifyPage`, `SignInWaitingPage`. Shared
  `WhatsAppHandoffView` control for the prefilled-message card, countdown pill, and the two
  buttons, so the two waiting screens and the two handoff screens do not diverge.
- `IOnboardingFlow` (singleton) owns the in-progress state (pending link kind, request time,
  matched account, remember-device) and routes `register`/`login` links: `App.OnAppLinkRequestReceived`
  calls `HandleAppLinkAsync` first and falls back to the legacy challenge callback. Links are
  accepted on the custom scheme (`southbaysoccer://auth/register|login?token=`) today and on the
  `AppLinkBaseUri` host once universal/app links land (M13.1). Token lives in memory only.
- `IOnboardingNavigator` pushes the onboarding pages on the Welcome Back `NavigationPage` stack
  (Shell does not exist before sign-in). `LinkWaitingPage` is shared by both flows.
- Debug builds honour `N9JABAY_ONBOARDING_SCREEN=<screen>` at launch to open any onboarding screen
  against Seed data without a WhatsApp round-trip.
- `IExternalLauncher.OpenWhatsAppMessageAsync(text)` using `https://wa.me/<digits>?text=<encoded>` (`PickupPalOptions.CreateWhatsAppMessageUri`).
- `IOnboardingClient` (validate, check email, register) and `IAuthenticationClient.CompleteLoginAsync`.
- Countdown is local presentation only; server expiry is authoritative.
- Typed configuration: bot number, app-link host, `termsVersion` (served by the Function App, not
  hardcoded in the client).

**Backend (Functions / Application / Infrastructure)**

- `IPickupPalOnboardingClient` (Infrastructure): validate registration token, check email,
  register with token, redeem login token, delete user. Presents the bot API key. Same URL-logging
  ban as `PickupPalUserClient`: request URIs on this client are never logged.
- Commands: `ValidateRegistrationTokenCommand`, `RegisterWithWhatsAppCommand`,
  `BeginPhoneSignInCommand` (replaces the token-issuing half of `SignInByPhoneCommand`),
  `CompleteWhatsAppLoginCommand`, `DeleteAccountCommand`.
- `PendingPhoneSignIn` record (Id, PickupPalUserId, PhoneNumberHash, ExpiresAt, ConsumedAt) so a
  login token can only complete the sign-in it was started for.
- Endpoints (anonymous unless noted): `POST auth/pickuppal/register/validate`,
  `POST auth/pickuppal/register`, `POST auth/pickuppal/phone/sign-in` (changed response),
  `POST auth/pickuppal/login/complete`, `GET auth/terms/current`, `DELETE profiles/me` (bearer).
- Rate limits per IP and per phone hash on every anonymous endpoint above (`INV-11`).

## Security (`NFR-Security`, `INV-11`)

- Tokens from links are single-use and 15 minutes on the Pickup Pal side; N9ja Bay never persists
  them and never puts them in logs, telemetry, or crash reports.
- The client never holds Pickup Pal credentials or the bot API key.
- Login completion is bound to the pending sign-in; a token for another user is rejected with a
  generic error.
- Password is sent once, over TLS, to the Function App, which forwards it to Pickup Pal and
  discards it. N9ja Bay stores no password.
- Not-found and mismatch responses do not reveal whether a phone or email exists beyond what the
  user typed themselves.
- Phone numbers are normalized to E.164 in the Function App before any Pickup Pal call, because
  Pickup Pal assumes US for 10-digit input.

## Test design - AUTH-10 slice

`Client.Tests`
- start sign-up makes no network call and shows the configured bot number and command text;
- WhatsApp handoff opens the launcher with the exact prefilled message; launcher failure keeps the
  user on the screen with recoverable copy;
- app link with a valid token routes to details and shows the masked number; expired/invalid
  routes to the expired screen; tokens never appear in logs;
- details form blocks submit on any invalid field and on a taken email without spending the token;
- successful registration completes sign-in once and navigates to welcome;
- phone sign-in with `verificationRequired` navigates to verify and stores no tokens;
- login link completes sign-in; mismatch shows a generic error and stores nothing;
- remember-device flag is passed through to the complete-login request.

`Application.Tests`
- register handler calls validate, then register, then sync, then group re-read, in that order, and
  issues tokens only after sync succeeds;
- complete-login handler rejects a token whose user differs from the pending sign-in, and rejects an
  expired or consumed pending sign-in;
- delete-account handler soft-deletes locally, calls Pickup Pal delete, and revokes tokens even if
  the Pickup Pal call fails (logged, retried by outbox).

`Functions.Tests`
- each new endpoint maps Pickup Pal error strings (both error body shapes) to RFC 7807 problems
  with safe details;
- rate limiting returns 429 with no body detail.

`Infrastructure.Tests`
- `IPickupPalOnboardingClient` sends the API key, parses both error shapes, and never logs URIs.
