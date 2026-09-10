---
name: pickuppal-account-creation
description: PickupPal account-creation API contract (web signup, WhatsApp pre-reg token, link code) and the rules N9ja Bay must follow when it adds signup
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

**Why:** PickupPal stays the identity source of truth ([[pickuppal-phone-sign-in]]), so N9ja Bay
signup means creating the PickupPal user through our Functions, then running our existing sync.
**How to apply:** Design signup as Functions endpoints that wrap these calls, prefer the WhatsApp
`!!register` direction on mobile because the account arrives already linked, and treat Apple's
account-deletion requirement (5.1.1(v)) as part of the same story.

Related: [[pickuppal-phone-sign-in]], [[pickuppal-groupchat-read-only]], [[m3-whatsapp-session-auth]]
