# M13 Onboarding Backend (AUTH-10)

Backend for WhatsApp-verified sign-up, verified sign-in, and account deletion. Spec:
`_specs/stories/AUTH-10-whatsapp-verified-onboarding/`. External contract:
`documentation/pickuppal-account-creation-api.md`.

## Local-first persistence rule

Our database is written **before** Pickup Pal is called, and a failed Pickup Pal call never deletes
the local row:

- `PlayerRegistration` (`Domain/Entities/Identity/`, soft-deleted) is upserted per phone hash with
  status `PendingExternal` before `POST register/whatsapp`; success marks it `Completed` with the
  Pickup Pal user id, failure marks it `ExternalFailed` with a safe failure code
  (`LastExternalError`) and bumps `ExternalAttemptCount`. Never stores the raw phone, the password,
  or the token. A retry with a fresh `!!register` token reuses the awaiting row.
- Pickup Pal unreachable (network, timeout, 5xx) returns **503 `upstream-unavailable`** and writes an
  `OutboxMessages` row (`PlayerRegistrationExternalFailed`). Nothing drains the outbox yet; the row
  is the reconciliation record. The player retries by sending `!!register` again (a new token).
- `PendingPhoneSignIn` (`Domain/Entities/Operations/`, immutable operational record, not soft-
  deleted) is written by `BeginPhoneSignInCommand` and consumed by `CompleteWhatsAppLoginCommand`.
  The client carries no pending id, so the binding is the redeemed Pickup Pal user id: a login token
  for a user with no live pending sign-in is a **mismatch (403)**.
- `DELETE profiles/me`: `LocalAccountDeletionService` soft-deletes the profile, emergency contacts,
  group links, and registrations, anonymizes and locks the identity user (synthetic unique email so
  the real one is free for re-registration), revokes every refresh token, then an outbox row
  (`PickupPalUserDeletionRequested`) is written before the Pickup Pal delete is attempted; failure
  leaves it `RetryScheduled` and still returns 204.
- Sign-out now revokes the presented refresh token's family (`IRefreshTokenRevocationService`).

## Pickup Pal routes (`PickupPal:Routes:*`, `PickupPalApiOptions`)

| Route | Default | Status |
|---|---|---|
| `RegisterValidate` | `api/users/register/whatsapp/validate` (`?token=`) | confirmed |
| `RegisterWithToken` | `api/users/register/whatsapp` | confirmed |
| `EmailLookup` | `api/users/email/{email}` (404 = available) | confirmed |
| `LoginRedeem` | `api/users/login/whatsapp` (`POST { token }`) | **UNCONFIRMED placeholder (M13.0)** |
| `DeleteUser` | `api/users/{id}` (`DELETE`; 404 = already gone) | **UNCONFIRMED placeholder (M13.0)** |

`PickupPal:ApiKey` is sent as `X-Api-Key` (header name configurable) when present; absent until the
bot API has one. **URI-logging ban** (same as `PickupPalUserClient`): the validate route carries the
token in the query string and the email lookup carries the email in the path, both forced by the
external contract. `PickupPalOnboardingClient` takes no `ILogger`, its `HttpClient` has no message
handlers, and no telemetry may record its request URIs (a test guards the constructor).

## Problem types

Token outcomes are RFC 7807 problems with an absolute `type` the MAUI client matches on:
`https://southbaysoccer/problems/onboarding-token-{expired (410) | invalid (404) |
already-registered (409, phone or email) | mismatch (403)}`. Details are fixed copy and never echo
the token. Pickup Pal error bodies come in two shapes (`error` string vs `error.message` object);
`PickupPalOnboardingClient` parses both and classifies by the literal strings in contract section 4.

## Settings (`Onboarding:*`, `OnboardingOptions` / `IOnboardingPolicy`)

- `RequireWhatsAppVerification` (default `true`): phone sign-in returns 202 `verificationRequired`
  and no tokens. Set `false` only for local development.
- `VerificationExemptPhoneNumbers`: comma-separated, normalized to `+digits` like `AdminPhoneNumbers`;
  for the App Review demo accounts, which get tokens straight from phone sign-in.
- `TermsVersion` (default `20250708`): served by `GET auth/terms/current` and required on register.
- `PendingSignInLifetime` (15 min), `RememberDeviceRefreshTokenLifetime` (30 days; the non-remembered
  default in `AuthenticationTokenIssuer.DefaultRefreshTokenLifetime` is also 30 days today).

Rate limits (`AnonymousRateLimits`, in-memory sliding window per Functions instance): 30/5 min per
IP on every anonymous auth endpoint, 5/15 min per phone on sign-in start, 10/15 min per email on the
availability check. Keys are SHA-256 hashes; nothing raw is held.

Legacy `auth/whatsapp/challenges*`, `IWhatsAppChallengeService`, and the `WhatsAppSignInChallenges`
table are removed (migration `AddOnboardingRegistrations` drops the table). The
`RequestWhatsAppChallenge*`/`VerifyWhatsAppChallengeRequest` contracts remain only because the MAUI
client still compiles against them.

Related: [[pickuppal-account-creation]], [[pickuppal-phone-sign-in]], [[functions-problem-details]],
[[m1-operational-records]]
