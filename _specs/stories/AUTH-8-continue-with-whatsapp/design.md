# AUTH-8 - Pickup Pal phone sign-in - Design

Realizes [`requirements.md`](requirements.md). Screen composition is in
[`AUTH-7 design`](../AUTH-7-welcome-back-screen/design.md); token issue/refresh mechanics are in
[`../../design.md`](../../design.md) section 6; this file specifies the phone sign-in flow.

## Flow

```text
WelcomeBackPageModel.RequestWhatsAppChallengeCommand
  -> validate PhoneNumber (international format); else set PhoneNumberError, send nothing
  -> IsBusy = true (block re-submit)
  -> IAuthenticationClient.SignInByPhoneAsync(phone)
  -> Function App calls Pickup Pal GET /api/users/phone/{digits}
  -> if not found: safe sign-up prompt, no tokens, stay signed out
  -> if found: sync ApplicationIdentityUser + PlayerProfile, preserving local role
  -> Function App returns SouthBaySoccer access + rotating refresh tokens (AUTH-3/AUTH-4)
  -> ISecureTokenStore persists tokens (platform secure storage)
  -> IAuthenticationNavigator replaces Welcome Back with the authenticated Sessions Shell (once)
```

## Components

- **Client:** `RequestWhatsAppChallengeCommand` currently owns the sign-in button behavior for XAML compatibility; it calls `IAuthenticationClient.SignInByPhoneAsync`, then `IAuthenticationCoordinator.CompleteSignInAsync` on success. International phone validation remains presentation validation only; server validation is authoritative.
- **Backend (Contracts/Functions/Application):** anonymous `POST /auth/pickuppal/phone/sign-in`, `SignInByPhoneRequest`, Pickup Pal user lookup, local identity/profile sync, SouthBaySoccer access-token issuance, and refresh-token rotation from `AUTH-3/AUTH-4`.
- **Infrastructure:** `IPickupPalUserClient` uses configurable `PickupPal:BaseUrl` and treats Pickup Pal as the profile source of truth. SouthBaySoccer stores local identity, email, role, token state, masked/hash phone, and `PickupPalUserId`; it does not store raw phone numbers.

## Security (`NFR-Security`, `INV-11`)

- Single in-flight sign-in per submit; duplicate taps coalesce.
- Authentication is established only when Pickup Pal returns a user and SouthBaySoccer issues tokens.
- Not-found responses stay safe and do not disclose phone/email beyond the user's own entered number.
- Never log the raw phone number, Pickup Pal email, access token, or refresh token; mask sensitive values in telemetry.

## Test design (`Client.Tests` / `Application.Tests` / `Infrastructure.Tests` / `Functions.Tests`) - AUTH-8 slice

- valid number invokes phone sign-in once; invalid number does not call the API and shows inline validation;
- repeated taps while busy produce exactly one sign-in request;
- not found shows the sign-up prompt, stores no tokens, and does not navigate;
- service failure preserves the number, re-enables the action, and shows non-sensitive copy;
- Pickup Pal user sync creates/updates identity/profile, persists email, keeps local role unchanged, and avoids duplicates;
- the anonymous Function endpoint returns tokens on success and safe problem details on not found.
