# AUTH-8 — Continue with WhatsApp · Design

Realizes [`requirements.md`](requirements.md). Screen composition is in
[`AUTH-7 design`](../AUTH-7-welcome-back-screen/design.md); token issue/refresh mechanics are in
[`../../design.md`](../../design.md) §6; this file specifies the challenge flow.

## Flow

```
WelcomeBackPageModel.RequestWhatsAppChallengeCommand
  -> validate PhoneNumber (international format) ; else set PhoneNumberError, send nothing
  -> IsBusy = true (block re-submit)
  -> IAuthenticationClient.RequestWhatsAppChallengeAsync(phone)   // one-time Pickup Pal challenge
  -> on failure/offline: show recoverable error, IsBusy = false, keep number
  -> on success: state = "awaiting deep link"

Deep-link callback (approved scheme)
  -> authentication coordinator verifies + exchanges the challenge at the Function App
  -> Function App returns SouthBaySoccer access + rotating refresh tokens (AUTH-3/AUTH-4)
  -> ISecureTokenStore persists tokens (platform secure storage)
  -> IAuthenticationNavigator replaces Welcome Back with the authenticated Sessions Shell (once)
```

## Components

- **Client:** `RequestWhatsAppChallengeCommand`, `PhoneNumber`, `PhoneNumberError`, `IsBusy`;
  `IAuthenticationClient`, `ISecureTokenStore`, `IAuthenticationNavigator`, and a deep-link
  authentication coordinator. International phone validation in the page model (presentation
  validation only; server is authoritative).
- **Backend (Contracts/Functions/Application):** a Pickup-Pal-backed challenge endpoint
  (`[AllowAnonymous]`) and a verify/exchange endpoint that mints SouthBaySoccer tokens; reuses the
  `ITokenService` + refresh-token rotation from `AUTH-3/AUTH-4`. Approved deep-link callback scheme
  is typed configuration.

## Security (`NFR-Security`, `INV-11`)

- Single in-flight challenge per submit; duplicate taps coalesce.
- Authentication is established **only** by a verified challenge exchange — never by returning from
  WhatsApp/browser. No authenticated route opens before verification.
- Never log the phone number, challenge, deep-link token, or tokens; mask in telemetry.

## Test design (`Client.Tests` / `Functions.Tests`) — AUTH-8 slice

- valid number invokes the challenge client; invalid number does not and shows inline validation;
- repeated taps while busy produce exactly one challenge request;
- request failure preserves the number and re-enables the action;
- a verified deep link exchanges, stores tokens securely, and navigates exactly once;
- (backend) verify/exchange issues rotating tokens and rejects an unverified/forged challenge.
