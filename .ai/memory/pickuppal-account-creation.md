---
name: pickuppal-account-creation
description: PickupPal account-creation contract plus the agreed N9ja Bay sign-up (!!register) and verified sign-in (!!login) flows; spec in AUTH-10
type: project
created: 2026-09-10
---

The PickupPal bot API owner supplied the full account-creation contract on 2026-09-10. The verbatim
reference lives at `documentation/pickuppal-account-creation-api.md`. Read it before designing the
N9ja Bay signup flow (planned for 1.1, after the 1.0 App Store review).

Durable facts to apply:

- **Two creation endpoints only.** `POST /api/users` (web, email + password, phone optional) and
  `POST /api/users/register/whatsapp` (phone comes from a 15-minute single-use `!!register` token).
  `POST /api/users/:id/link-code` + `!!link <code>` attaches WhatsApp to an existing account.
- **The bot API has no auth.** It is gated only by CORS. **N9ja Bay's MAUI client must never call it
  directly**; every PickupPal call goes through SouthBaySoccer Functions, same as today's phone
  lookup. Password-reset request returns the raw token, so it is server-to-server only.
- **Always send `password` (≥ 6 chars, enforced client-side) and `termsVersion` + `termsAcceptedAt`.**
  Omitting password creates an account that can never log in. Current terms version is `"20250708"`;
  keep it in one Functions setting, not a literal.
- **Pre-check `GET /api/users/email/:email` before WhatsApp registration.** A duplicate-email 400
  burns the token and the user must `!!register` again.
- **A 201 does not mean linked.** Group auto-join and history back-fill fail silently; refetch
  `/api/users/:id/groups` and `/whatsapp-identity` before showing membership.
- **Two error body shapes.** `error` is a string on 4xx and an object `{message,status}` on 500.
- **Phone normalization is digit-stripping.** 10 digits assume US and get `1` prepended. Normalize
  to E.164 on our side before calling, consistent with `PhoneNumberValidator`.
- **No email verification exists anywhere in PickupPal.** WhatsApp linking is the only proof of
  control, and it proves the phone, not the email. The link gate on web is client-side, fails open,
  and is skippable; the API enforces nothing.

## Agreed N9ja Bay flows (2026-09-14)

Spec: `_specs/stories/AUTH-10-whatsapp-verified-onboarding/`. Wireframes: screens `signup-*`,
`signin-verify`, `signin-waiting` in `documentation/mobile-wireframes.html`.

- **The bot cannot message first (WhatsApp rule, confirmed by the Pickup Pal developer).** Every
  proof of phone ownership starts with the player sending the bot a message that N9ja Bay prefills
  through a `wa.me` link. Bot-initiated one-time codes are not an option; do not propose them again.
- **Sign-up:** app prefills `!!register source=n9jabay`; bot mints the existing pre-reg token and replies
  with an N9ja Bay app link (`/register?token=`) instead of the web URL; app link opens the details
  form; Functions call validate, email check, then `register/whatsapp`, then the normal sync and
  token issue. Account arrives linked. Phone is verified before anything is created.
- **Sign-in:** phone lookup no longer issues tokens on a fresh device. It returns
  `verificationRequired`; app prefills `!!login source=n9jabay`; bot replies with `/login?token=`; Functions
  redeem it, check it against a server-side `PendingPhoneSignIn`, then issue tokens. "Remember this
  device" is a 30-day refresh token, revoked on sign-out.
- **Pickup Pal has shipped (2026-09-16, `documentation/pickuppal-mobile-signup-guide.md`):**
  `!!register source=n9jabay` replies with `https://n9jabay.app/register?token=`; they need us to
  confirm the domain so they can set `CLIENT_REGISTER_URLS`. Full endpoint catalogue is
  `documentation/pickuppal-api.postman.json` (129 endpoints). `DELETE api/users/{id}` is confirmed.
- **Still missing on Pickup Pal:** any `!!login` command or login-token redemption endpoint. Their
  position is that returning users sign in with `POST api/users/auth { login: email-or-phone,
  password }`. Decision (Option A): keep our verified sign-in code dormant behind
  `Onboarding:RequireWhatsAppVerification=false`; phone lookup stays the production sign-in.
- **App-link host decision (2026-09-16):** `https://n9jabay.desolatravels.com` (subdomain of the
  owner's existing Azure App Service Domain; a dedicated `n9jabay.app` was declined for now). GitHub
  Pages serves `documentation/CNAME` + `.well-known/` from `main`; DNS is a CNAME `n9jabay` →
  `tobikareem.github.io` in the Azure DNS zone. `assetlinks.json` still needs the Play app-signing
  SHA-256; the Apple App ID needs the Associated Domains capability before release signing.
- **Deletion decision:** `DELETE profiles/me` removes N9ja Bay data only by default; the Pickup Pal
  account is deleted only when the player opts in (`?alsoDeletePickupPal=true`).
- **Apple:** in-app sign-up triggers the 5.1.1(v) account-deletion requirement; it is part of the
  same milestone, not a follow-up.

**Why:** PickupPal stays the identity source of truth ([[pickuppal-phone-sign-in]]), so N9ja Bay
signup means creating the PickupPal user through our Functions, then running our existing sync.
**How to apply:** Design signup as Functions endpoints that wrap these calls, prefer the WhatsApp
`!!register` direction on mobile because the account arrives already linked, and treat Apple's
account-deletion requirement (5.1.1(v)) as part of the same story.

Related: [[pickuppal-phone-sign-in]], [[pickuppal-groupchat-read-only]], [[m3-whatsapp-session-auth]]
