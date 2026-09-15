# AUTH-10 - WhatsApp-verified onboarding - Tasks

AUTH-10 is milestone **M13** (roadmap: [`../../tasks.md`](../../tasks.md)). Do not start M13.2 or
later until M13.0 is confirmed by the Pickup Pal developer; M13.1 and M13.3 can proceed against a
fake client.

- [ ] **M13.0** External: confirm with Pickup Pal the five prerequisites in `design.md` (client
  argument on `!!register`/`!!login`, login-token redemption endpoint, bot API key, deletion
  endpoint, reply-link host). Record the agreed paths in
  `documentation/pickuppal-account-creation-api.md` and `.ai/memory/pickuppal-account-creation.md`.
  — Stories: `AUTH-10` · Projects: none · Depends on: nothing.

- [ ] **M13.1** Client: app links. Register the app-link host on iOS (associated domains +
  `apple-app-site-association`) and Android (intent filter + `assetlinks.json`). Add
  `IAppLinkRouter` that extracts `token` from `/register` and `/login` links and dispatches to a
  page model. Token is held in memory only.
  — Stories: `AUTH-10` · Projects: MAUI client · Depends on: nothing.

- [x] **M13.2** Backend: `IPickupPalOnboardingClient` in Infrastructure with API-key auth, E.164
  normalization, both error-shape parsing, and the URI-logging ban. Fake implementation for tests
  and Seed mode.
  — Stories: `AUTH-10` · Projects: Infrastructure, Application · Depends on: M13.0.

- [x] **M13.3** Client: sign-up screens. `SignUpStartPage`, `SignUpWaitingPage`,
  `SignUpExpiredPage`, `SignUpDetailsPage`, `SignUpWelcomePage`, the shared `WhatsAppHandoffView`
  control, `IExternalLauncher.OpenWhatsAppAsync`, and `IOnboardingClient` with a Seed
  implementation (custom-scheme links routed by `IOnboardingFlow`; universal links remain M13.1). Welcome Back gains "Create your account" and drops the web sign-up copy. Must
  match wireframe screens `signup-*`.
  — Stories: `AUTH-10` · Projects: MAUI client · Depends on: M13.1.

- [x] **M13.4** Backend: registration endpoints. `POST auth/pickuppal/register/validate`,
  `POST auth/pickuppal/register`, `GET auth/terms/current`; `RegisterWithWhatsAppCommand` runs
  validate → email check → register → sync → group re-read → token issue. Rate limits per IP and
  phone hash.
  — Stories: `AUTH-10` · Projects: Application, Functions · Depends on: M13.2.

- [x] **M13.5** Backend: verified sign-in. `BeginPhoneSignInCommand` returns
  `verificationRequired` with a `PendingPhoneSignIn` record instead of tokens;
  `CompleteWhatsAppLoginCommand` redeems the login token, checks it against the pending record,
  syncs, and issues tokens with a 30-day refresh lifetime when `rememberDevice` is set. Remove the
  legacy `auth/whatsapp/challenges*` endpoints and `IWhatsAppChallengeService`.
  — Stories: `AUTH-10` · Projects: Application, Infrastructure, Functions · Depends on: M13.2.

- [x] **M13.6** Client: sign-in verification screens. `SignInVerifyPage`, `SignInWaitingPage`,
  `IAuthenticationClient.CompleteLoginAsync`, remember-device toggle, and Welcome Back routing on
  `verificationRequired`. Rename `RequestWhatsAppChallengeCommand` to `SignInCommand`.
  — Stories: `AUTH-10` · Projects: MAUI client · Depends on: M13.1, M13.5.

- [ ] **M13.7** Account deletion. `DELETE profiles/me` (bearer) soft-deletes local records, calls
  Pickup Pal delete through the outbox, revokes refresh tokens; Profile screen gains "Delete
  account" with confirmation. *(Backend done; the Profile screen action is still open.)*
  — Stories: `AUTH-10` · Projects: Application, Functions, MAUI client · Depends on: M13.2.

- [ ] **M13.8** Tests per `design.md` "Test design" across Client, Application, Functions, and
  Infrastructure test projects. Full suite green.
  — Stories: `AUTH-10` · Depends on: M13.3–M13.7.

- [ ] **M13.10** Backend: block `IPickupPalUserSyncService` (phone sign-in, login completion,
  registration) for a Pickup Pal user id whose `PickupPalUserDeletionRequested` outbox row is still
  unprocessed, so a re-sign-in cannot resurrect a deleted account before Pickup Pal deletes it; add
  a purge policy for `PendingPhoneSignIns`.
  — Stories: `AUTH-10` · Projects: Application, Infrastructure · Depends on: M13.7.

- [ ] **M13.9** Release prep. Update App Store review notes (sign-up now in-app; deletion path),
  privacy policy (WhatsApp handoff, data sent to Pickup Pal), and re-run the demo-account walkthrough.
  — Stories: `AUTH-10` · Depends on: M13.8.

**Done when:** a new player can create a linked account without leaving the app except to send one
WhatsApp message; a returning player on a fresh device must complete the `!!login` link before
tokens are issued; a remembered device signs in silently until sign-out; account deletion works
end to end; no token, password, phone number, or Pickup Pal URI appears in logs; and every
scenario in `requirements.md` has a passing test.
