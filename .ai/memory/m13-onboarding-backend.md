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
  `OutboxMessages` row (`PlayerRegistrationExternalFailed`). Registration failures are audit-only;
  the player retries with a new `!!register` token. The M14 outbox processor drains account
  deletion and RSVP/session sync requests, not registration failures.
- `PendingPhoneSignIn` (`Domain/Entities/Operations/`, immutable operational record, not soft-
  deleted) is written by `BeginPhoneSignInCommand` and consumed by `CompleteWhatsAppLoginCommand`.
  The client carries no pending id, so the binding is the redeemed Pickup Pal user id: a login token
  for a user with no live pending sign-in is a **mismatch (403)**.
- `DELETE profiles/me`: `LocalAccountDeletionService` soft-deletes the profile, emergency contacts,
  group links, and registrations, anonymizes and locks the identity user (synthetic unique email so
  the real one is free for re-registration), and revokes every refresh token. A callback records
  the opt-in `PickupPalUserDeletionRequested` intent (or local-only audit) in the same serializable
  SQL execution-strategy transaction. Only after commit is Pickup Pal called; failure leaves the
  intent retryable. Retry reads include the deleted profile so an ambiguous commit preserves the
  provider id and reuses the same outbox key.
- The backend sign-out endpoint revokes the presented refresh token's family
  (`IRefreshTokenRevocationService`); MAUI sign-out still needs to call that endpoint.
- `RegisterWithWhatsAppCommand` records `ExternalCreated` + `PickupPalUserId` with its own save the
  moment Pickup Pal returns 201, before local sync, so a sync failure never orphans an upstream account.
- `PendingPhoneSignIn` carries a SQL row version; completion consumes **every** live row for the
  user, and a row-version conflict (same link opened twice) is reported as a mismatch.
- `DeleteAccountCommandHandler` looks the deletion outbox row up by idempotency key first, so a
  double tap after local deletion reuses the row instead of tripping the unique index.
- Refresh lifetimes: session sign-in (no remember-device) = `Onboarding:SessionRefreshTokenLifetime`
  (12 h); remember-device = 30 d; rotation keeps the family's own lifetime
  (`ExpiresAtUtc - CreatedAt` of the presented token), so refreshing never extends a session token.

## Pickup Pal routes (`PickupPal:Routes:*`, `PickupPalApiOptions`)

| Route | Default | Status |
|---|---|---|
| `RegisterValidate` | `api/users/register/whatsapp/validate` (`?token=`) | confirmed |
| `RegisterWithToken` | `api/users/register/whatsapp` | confirmed |
| `EmailLookup` | `api/users/email/{email}` (404 = available) | confirmed |
| `LoginRedeem` | `api/users/login/whatsapp` (`POST { token }`) | **Does not exist** (2026-09-16 contract). Verification flow dormant; `Onboarding:RequireWhatsAppVerification` default **false** |
| `DeleteUser` | `api/users/{id}` (`DELETE`; 404 = already gone) | confirmed (Postman contract). Called **only** when the player opts in via `DELETE profiles/me?alsoDeletePickupPal=true`; default deletes N9ja Bay data only and writes an `N9jaBayAccountDeleted` audit outbox row |

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

- `RequireWhatsAppVerification` (default `false`): production phone lookup stays enabled by the
  September 16 decision because Pickup Pal has no login redemption route. When enabled, sign-in
  returns 202 `verificationRequired` and no tokens (except configured exemptions). Do not enable
  until the external endpoint and pending-flow binding are complete.
- `VerificationExemptPhoneNumbers`: comma-separated, normalized to `+digits` like `AdminPhoneNumbers`;
  for the App Review demo accounts, which get tokens straight from phone sign-in.
- `TermsVersion` (default `20250708`): served by `GET auth/terms/current` and required on register.
- `PendingSignInLifetime` (15 min), `RememberDeviceRefreshTokenLifetime` (30 days),
  `SessionRefreshTokenLifetime` (12 hours). `AuthenticationTokenIssuer.DefaultRefreshTokenLifetime`
  (30 days) is only used by the verification-exempt / verification-disabled phone sign-in path.

Rate limits (`AnonymousRateLimits`, in-memory sliding window per Functions instance): 30/5 min per
IP on every anonymous auth endpoint, 5/15 min per phone on sign-in start (keyed on the same
normalized digits the Pickup Pal lookup uses), 10/15 min per email on the availability check. The
IP key prefers `X-Azure-ClientIP`, then the **last** `X-Forwarded-For` hop, port stripped. Keys are
SHA-256 hashes; nothing raw is held.

## Known gaps (documented, not fixed)

- Access tokens stay valid for up to `Authentication:Jwt:AccessTokenLifetime` (15 min) after
  account deletion or sign-out; only refresh tokens are revoked immediately.
- Re-signing in while a `PickupPalUserDeletionRequested` outbox row is still unprocessed would let
  `PickupPalUserSyncService` recreate the local profile from the still-existing Pickup Pal user.
  Follow-up **M13.10** in the story tasks: block sync while a deletion is pending.
- `PendingPhoneSignIns` has no retention/purge yet (immutable operational record; grows with every
  sign-in start). Same purge-service gap as the other operational tables.
- `DeleteUser` treats 404 as already deleted; `PickupPalUserDeletionOutboxHandler` retries failures.
- Account-deletion UI remains open. Verified login still binds to any live pending sign-in for
  the redeemed user, not a pending id carried by the initiating device; fix before enabling it.
- Migration `AddOnboardingRegistrations` drops `WhatsAppSignInChallenges` **with its data** (ephemeral
  challenge rows from the retired flow); it is not recoverable after deploy.

Legacy `auth/whatsapp/challenges*`, `IWhatsAppChallengeService`, and the `WhatsAppSignInChallenges`
table are removed (migration `AddOnboardingRegistrations` drops the table). The
`RequestWhatsAppChallenge*`/`VerifyWhatsAppChallengeRequest` contracts remain only because the MAUI
client still compiles against them.

Related: [[pickuppal-account-creation]], [[pickuppal-phone-sign-in]], [[functions-problem-details]],
[[m1-operational-records]]
