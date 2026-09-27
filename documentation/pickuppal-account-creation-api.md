# PickupPal — Account Creation: Endpoints & Workflows

Audience: a developer implementing a new client (mobile app, alternate web client) against the
existing PickupPal backend. Supplied by the PickupPal API owner on 2026-09-10; kept here as the
reference contract for the N9ja Bay account-creation work (see `.ai/memory/pickuppal-account-creation.md`
for the distilled rules and the review notes).

Everything below is the bot service (`pickuppal-bot/`), an Express API. `pickuppal-web` is only one
consumer of it — it holds no user data of its own.

## 1. Service basics

| Item | Value |
|------|-------|
| Base URL (env) | `NEXT_PUBLIC_BOT_SERVICE_URL` on web, `BOT_API_URL` in Next.js server routes |
| Local default | `http://localhost:3005` (web client) / `http://localhost:3001` (Next server routes) — see gotcha #1 |
| Content type | `application/json` on every request with a body |
| Auth | None. No API key, no bearer token. Access is gated only by CORS (`CORS_ORIGIN`, comma-separated; `*` if unset) — see gotcha #2 |
| Route prefix | `/api` (user routes mounted at `/api/users`) |
| Health | `GET /health-simple` → `{ status: "healthy" \| "degraded", database, whatsapp }` |

Source of truth for endpoint paths on the web side: `pickuppal-web/src/config/api.ts` (`API_ENDPOINTS`).
Routes: `pickuppal-bot/api/routes/userRoutes.js`. Handlers: `pickuppal-bot/api/controllers/userController.js`.

## 2. The three ways an account gets created

There are exactly two account-creation endpoints, plus a third flow that links an existing account to WhatsApp.

```
A. Web signup            POST /api/users
B. WhatsApp signup       POST /api/users/register/whatsapp   (requires pre-reg token)
C. Link existing account POST /api/users/:id/link-code  →  user types !!link <code> in WhatsApp
```

### Flow A — Web signup (email + password)

```
User fills signup form
  → client-side validation (email format, password ≥ 6, first/last name, terms checkbox)
  → POST /api/users
  → 201 with the created user (password + memberCode stripped)
  → client signs in (NextAuth credentials provider → POST /api/users/auth)
  → redirect to WhatsApp-connect step (flow C)
```

Notes:

- `phoneNumber` is optional here. Most web users have no phone on the account until they link WhatsApp.
- Terms acceptance is mandatory and enforced server-side: both `termsVersion` and `termsAcceptedAt`
  must be present or you get a 400. Current version string used by the web client: `"20250708"`
  (`TERMS_VERSION` in `src/app/auth/signup/page.tsx`).
- The account is created with no WhatsApp identity. Until flow C runs, the user's past WhatsApp game
  history is not attached to them. The client then pushes the user to the linking screen — see §8 for
  that onboarding gate and exactly how weakly it is enforced.

### Flow B — WhatsApp self-registration (pre-reg token)

This is the flow triggered from inside WhatsApp. The token is short-lived and is itself the
authentication — it proves the requester controls that WhatsApp number, so no other auth is required
on these two endpoints.

```
1. User DMs the bot:  !!register       (also triggered by !!joinleague <CODE>
                                        when the sender has no account)
2. Bot creates a WhatsAppPreRegToken (UUID v4, TTL 15 min) bound to the
   sender's JID + resolved phone number, and DMs back:
       {WEB_APP_URL}/register?token=<uuid>
3. Client opens /register, calls
       GET /api/users/register/whatsapp/validate?token=...
   to check the token before rendering the form (and to prefill the phone number).
4. User fills first/last name, email, password, accepts terms.
5. POST /api/users/register/whatsapp  with the token in the body.
6. On success the server, in one request:
       - redeems the token (single use, marks redeemedAt)
       - creates the User with the phone number from the token
       - creates WhatsAppIdentity rows for the resolved JID and the raw @lid
       - back-fills userId onto past GamePlayer rows (by phone and by JID)
       - auto-joins the user to every bot-registered GroupChat they're in
       - resolves any pending league registrations for that JID/phone
7. Client signs in via NextAuth exactly as in flow A.
```

Important: steps under 6 after the user is created are wrapped in `try {} catch {}` and fail
silently. A 201 means the account exists; it does not guarantee groups/history were linked.

Bot-side guard: if the sender already has an account (matched by phone or JID), `!!register` replies
"You already have a PickupPal account!" and no token is issued. `!!register` from a JID that can't be
resolved past an `@lid` is rejected with "To register, please DM me directly with !!register."

### Flow C — Linking WhatsApp to an existing account

Not account creation, but it is the second half of onboarding and clients need it.

```
1. POST /api/users/:id/link-code        → { code: "A1B2C3", expiresAt }
   6-char code, A–Z0–9, TTL 15 minutes, one live code per user
   (generating a new one deletes the previous).
2. User sends  !!link A1B2C3  to the bot (DM or group).
3. Bot redeems it, creates the WhatsAppIdentity, then runs the same
   back-fill / auto-join / league-resolve steps as flow B.
4. Client polls/refetches GET /api/users/:id/whatsapp-identity
   → { linked: boolean, jids: string[] }
```

Bot replies on redemption:

- success → "✅ Your WhatsApp account has been successfully linked…"
- expired → "⏰ That link code has expired. Please generate a new one at www.pickuppal.xyz"
- invalid → "❌ Invalid link code. Please check the code and try again…"

Codes are matched uppercase — the handler uppercases input, so accept lowercase entry in your UI.

## 3. Endpoint reference

### `POST /api/users` — create account (web signup)

Middleware: `validateUserCreation`.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `email` | string | ✅ | must match `/^[^\s@]+@[^\s@]+\.[^\s@]+$/`; unique |
| `firstName` | string | ✅ | |
| `lastName` | string | ✅ | |
| `termsVersion` | string | ✅ | e.g. `"20250708"` |
| `termsAcceptedAt` | ISO 8601 string | ✅ | parsed with `new Date()` |
| `password` | string | ❌ | hashed with bcrypt, 10 rounds. If omitted the account has no password and can never log in via `/api/users/auth`. Web client always sends it; enforce ≥ 6 chars client-side — the server does not check length here |
| `phoneNumber` | string | ❌ | normalized (digits only, US 10-digit gets `1` prefixed); unique |
| `nickName` | string | ❌ | |
| `profilePicture` | string (URL) | ❌ | |

Success: `201` with the user object, `password` and `memberCode` removed.

### `GET /api/users/register/whatsapp/validate?token=<uuid>`

No auth. Always 200 unless the `token` param is missing.

```json
{ "valid": true,  "phoneNumber": "16502205416" }   // phoneNumber may be null
{ "valid": false, "reason": "invalid" }            // unknown or already redeemed
{ "valid": false, "reason": "expired" }
```

### `POST /api/users/register/whatsapp`

Middleware: `validateWhatsAppRegistration`.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `token` | string (UUID) | ✅ | single-use, 15-min TTL |
| `firstName` | string | ✅ | |
| `lastName` | string | ✅ | |
| `email` | string | ✅ | same regex as above; unique |
| `termsVersion` | string | ✅ | |
| `termsAcceptedAt` | ISO 8601 | ✅ | |
| `password` | string | ❌ | same caveat as flow A |

`phoneNumber` is not accepted in the body — it comes from the token.

Success: `201` with the created user (`password`, `memberCode` stripped).

⚠️ The token is redeemed before the email/phone uniqueness checks. A duplicate-email 400 therefore
burns the token: the user must run `!!register` again to get a new link. Validate email availability
client-side first via `GET /api/users/email/:email` (404 = available).

### `POST /api/users/auth` — login

Body: `{ "login": "<email or phone>", "password": "..." }`. `login` is tried as email first, then as
phone number. Success: `200` with the user object (stripped). Failure: `401`.

The endpoint returns the same `401 Invalid credentials` for unknown user, no password set, and wrong
password — no account enumeration.

### `POST /api/users/:id/link-code`

→ `200 { "code": "A1B2C3", "expiresAt": "2026-09-09T18:15:00.000Z" }`, or `404` if the user doesn't exist.

### `GET /api/users/:id/whatsapp-identity`

→ `200 { "linked": true, "jids": ["16502205416@s.whatsapp.net", "123456@lid"] }`

### `DELETE /api/users/:id/whatsapp-identity`

→ `200 { "success": true }` — removes all identities for the user.

### Lookup helpers (useful for pre-flight uniqueness checks)

- `GET /api/users/email/:email` → 200 user | 404
- `GET /api/users/phone/:phoneNumber` → 200 user | 404
- `GET /api/users/:id` → 200 user | 404

### Password reset

| Endpoint | Body / params | Response |
|----------|---------------|----------|
| `POST /api/users/password-reset/request` | `{ email }` | `200 { created: false }` if no such user (deliberate — no enumeration), else `200 { created: true, token, expiresAt }` |
| `GET /api/users/password-reset/validate/:token` | — | `200 { valid: true, email }` or `{ valid: false, reason }` |
| `POST /api/users/password-reset/confirm` | `{ token, newPassword }` | `200 { message: "Password reset successful" }` |

Token: 32 random bytes hex, TTL 1 hour, single use. Requesting a new token marks all previous unused
tokens for that email as used.

⚠️ `password-reset/request` returns the raw token in the HTTP response — it does not send the email.
The web client's own Next.js route (`/api/auth/forgot-password`) calls this server-side, then emails
the link and returns only a generic message to the browser. Any client must do the same: never call
this endpoint from an end-user device, or the reset token is handed straight to whoever asks for it.

## 4. Error responses

There is no application-level error code system. Errors are identified by HTTP status plus the
literal `error` string. Two shapes exist:

Shape 1 — validation / handler errors (4xx), thrown by controllers & middleware:

```json
{ "error": "Missing required fields", "details": { "email": "Email is required" } }
```

`details` is either an object (per-field) or a plain string, and is absent on some errors.

Shape 2 — unhandled errors, produced by `errorHandler.js` (usually 500). Note the nesting differs:

```json
{ "error": { "message": "Internal Server Error", "status": 500 } }
```

`stack` is added only when `NODE_ENV === "development"`.

Client parsers must handle both: `typeof body.error === "string" ? body.error : body.error?.message`.

### Exhaustive list for the account-creation surface

| Status | `error` string | Endpoint | Trigger |
|--------|----------------|----------|---------|
| 400 | Missing required fields | `POST /api/users` | any of email, firstName, lastName, termsVersion, termsAcceptedAt missing. `details` names each |
| 400 | Invalid email format | `POST /api/users` | email fails regex |
| 400 | Invalid phone number | `POST /api/users` | `details` is one of `Phone number is required` / `Phone number must be at least 10 digits` |
| 400 | User with this email already exists | `POST /api/users` | email taken |
| 400 | Missing required fields | `POST /api/users/register/whatsapp` | any of token, firstName, lastName, email, termsVersion, termsAcceptedAt missing |
| 400 | Invalid email format | `POST /api/users/register/whatsapp` | email fails regex |
| 400 | Invalid registration token | `POST /api/users/register/whatsapp` | token unknown or already redeemed |
| 400 | Registration link has expired | `POST /api/users/register/whatsapp` | past `expiresAt` (token is also burned) |
| 400 | An account with this email already exists | `POST /api/users/register/whatsapp` | email taken (token already burned) |
| 400 | An account with this phone number already exists | `POST /api/users/register/whatsapp` | phone from token taken (token already burned) |
| 400 | `{ valid: false, reason: "Token is required" }` | `GET …/validate` | `token` query param missing — note this returns 400 with the `valid` shape, not the `error` shape |
| 400 | Login and password are required | `POST /api/users/auth` | missing field |
| 401 | Invalid credentials | `POST /api/users/auth` | unknown login, no password on account, or bad password |
| 400 | Email is required | `POST …/password-reset/request` | missing email |
| 400 | Token and new password are required | `POST …/password-reset/confirm` | missing field |
| 400 | Password must be at least 6 characters | `POST …/password-reset/confirm` | short password |
| 400 | Reset token is used / Reset token is expired | `POST …/password-reset/confirm` | consumed or stale token |
| 404 | Reset token is not_found | `POST …/password-reset/confirm` | unknown token |
| 404 | User not found | `GET/PUT/DELETE /api/users/:id`, `/link-code`, `/games`, `/groups` | unknown id |
| 400 | User with this email already exists | `PUT /api/users/:id` | email taken by another user |
| 400 | Invalid phone number format | `PUT /api/users/:id` | normalization threw; `details` carries the message |
| 400 | User with this phone number already exists | `PUT /api/users/:id` | phone taken |
| 400 | No valid fields provided for update | `PUT /api/users/:id` | body had none of the allowed fields |
| 500 | nested `{ error: { message, status } }` | any | uncaught exception (e.g. Prisma P2002 unique violation that slipped past a pre-check) |

Reason strings you will see inside `{ valid: false, reason }` bodies: `invalid`, `expired`
(pre-reg tokens); `not_found`, `used`, `expired` (password reset).

## 5. Sample payloads

Phone numbers in the samples are placeholders. The only real number is the PickupPal bot,
`+1 (650) 220-5416` (`16502205416`).

### A. Web signup

```bash
curl -X POST http://localhost:3005/api/users \
  -H 'Content-Type: application/json' \
  -d '{
    "email": "maya.torres@example.com",
    "password": "correct-horse-battery",
    "firstName": "Maya",
    "lastName": "Torres",
    "nickName": "Mays",
    "termsVersion": "20250708",
    "termsAcceptedAt": "2026-09-09T17:42:11.318Z"
  }'
```

201 Created:

```json
{
  "id": "clx8f2k9v0000qwer1234abcd",
  "email": "maya.torres@example.com",
  "firstName": "Maya",
  "lastName": "Torres",
  "nickName": "Mays",
  "phoneNumber": null,
  "profilePicture": null,
  "termsVersion": "20250708",
  "termsAcceptedAt": "2026-09-09T17:42:11.318Z",
  "createdAt": "2026-09-09T17:42:11.512Z",
  "updatedAt": "2026-09-09T17:42:11.512Z"
}
```

400 Bad Request (missing terms + no last name):

```json
{
  "error": "Missing required fields",
  "details": {
    "lastName": "Last name is required",
    "termsVersion": "Terms version is required",
    "termsAcceptedAt": "Terms acceptance timestamp is required"
  }
}
```

400 Bad Request (duplicate):

```json
{ "error": "User with this email already exists" }
```

### B. WhatsApp registration

Validate first:

```bash
curl 'http://localhost:3005/api/users/register/whatsapp/validate?token=8f14e45f-ceea-467a-9a1c-2b3d4e5f6a7b'
```

```json
{ "valid": true, "phoneNumber": "15550001234" }
```

Then register:

```bash
curl -X POST http://localhost:3005/api/users/register/whatsapp \
  -H 'Content-Type: application/json' \
  -d '{
    "token": "8f14e45f-ceea-467a-9a1c-2b3d4e5f6a7b",
    "firstName": "Andre",
    "lastName": "Silva",
    "email": "andre.silva@example.com",
    "password": "pickup-ball-2026",
    "termsVersion": "20250708",
    "termsAcceptedAt": "2026-09-09T17:50:03.001Z"
  }'
```

201 Created — note `phoneNumber` is populated from the token, not the body:

```json
{
  "id": "clx8f9a2b0001qwer5678efgh",
  "email": "andre.silva@example.com",
  "firstName": "Andre",
  "lastName": "Silva",
  "nickName": null,
  "phoneNumber": "15550001234",
  "profilePicture": null,
  "termsVersion": "20250708",
  "termsAcceptedAt": "2026-09-09T17:50:03.001Z",
  "createdAt": "2026-09-09T17:50:03.204Z",
  "updatedAt": "2026-09-09T17:50:03.204Z"
}
```

400 cases:

```json
{ "error": "Registration link has expired" }
{ "error": "Invalid registration token" }
{ "error": "An account with this phone number already exists" }
```

### C. Login

```bash
curl -X POST http://localhost:3005/api/users/auth \
  -H 'Content-Type: application/json' \
  -d '{ "login": "maya.torres@example.com", "password": "correct-horse-battery" }'
```

200 → same user shape as above. 401 → `{ "error": "Invalid credentials" }`.

Phone login works too — `{"login": "15550001234", "password": "..."}` — and the lookup normalizes,
so `(555) 000-1234` also resolves.

### D. Link WhatsApp after signup

```bash
curl -X POST http://localhost:3005/api/users/clx8f2k9v0000qwer1234abcd/link-code
```

```json
{ "code": "K7M2QX", "expiresAt": "2026-09-09T18:05:00.000Z" }
```

User sends `!!link K7M2QX` to +1 (650) 220-5416. Then:

```bash
curl http://localhost:3005/api/users/clx8f2k9v0000qwer1234abcd/whatsapp-identity
```

```json
{ "linked": true, "jids": ["15550001234@s.whatsapp.net", "88123456789012@lid"] }
```

### E. Password reset (server-to-server only)

```bash
curl -X POST http://localhost:3005/api/users/password-reset/request \
  -H 'Content-Type: application/json' -d '{ "email": "maya.torres@example.com" }'
```

```json
{
  "created": true,
  "token": "3f7c1e9b8a2d4f60b5c7e1a9d3f8b2c40e6a1d5f9b3c7e2a4d6f8b0c2e4a6d8f",
  "expiresAt": "2026-09-09T18:45:00.000Z"
}
```

Email the user `{WEB_APP_URL}/auth/reset-password?token=<token>&email=<encoded>`, then:

```bash
curl -X POST http://localhost:3005/api/users/password-reset/confirm \
  -H 'Content-Type: application/json' \
  -d '{ "token": "3f7c1e9b…", "newPassword": "new-strong-password" }'
```

```json
{ "message": "Password reset successful" }
```

## 6. Data model (relevant tables)

`pickuppal-bot/prisma/schema.prisma`

- **User** — `id` (cuid), `email` (unique), `password` (nullable, bcrypt), `phoneNumber` (nullable,
  unique, digits-only with country code), `firstName`, `lastName`, `nickName`, `profilePicture`,
  `termsVersion`, `termsAcceptedAt`, `memberCode` (nullable, unique).
- **WhatsAppIdentity** — `userId` → `whatsappJid` (unique). A user can have several: the phone JID
  (`<digits>@s.whatsapp.net`) and the privacy-mode `@lid`. Both are needed to match group participant lists.
- **WhatsAppPreRegToken** — `token` (unique), `whatsappJid` (unique), `rawLidJid`, `phoneNumber`,
  `redeemedAt`, `expiresAt`. Creating a token for a JID deletes any prior token for that JID.
- **WhatsAppLinkCode** — `userId` (unique), `code` (unique), `expiresAt`.
- **PasswordResetToken** — `token` (unique), `email`, `expiresAt`, `usedAt`.

### Response shape

`password` and `memberCode` are stripped by hand in each controller method
(`const { password: _, memberCode: __, ...userResponse } = user`). Every user payload the API returns
should be free of both — but this is manual, so treat a leaked field as a backend bug, not a contract.

`memberCode` is a credential (it lets anyone add its owner to a game). It is served only from
`GET /api/users/:id/member-code`, which mints one on first read.

## 7. Gotchas for the implementer

1. **The two default ports disagree.** `pickuppal-web/src/config/api.ts` defaults to `:3005`; the
   Next.js server routes default `BOT_API_URL` to `:3001`. Set both env vars explicitly rather than
   trusting either default.
2. **The API is unauthenticated.** Any caller who can reach it can read every user (`GET /api/users`),
   create accounts, and mint password reset tokens. In production it is protected only by
   `CORS_ORIGIN`, which does not stop non-browser clients. A mobile client shipping this base URL is
   publishing it. Raise this before building anything that widens the exposure.
3. **`password` is optional on both create endpoints.** Omitting it produces a permanently
   unloginnable account. Always send one, ≥ 6 chars.
4. **Password length is only enforced on reset, not on signup.** Enforce it client-side.
5. **A failed WhatsApp registration burns the token.** Pre-check email availability, and on a
   duplicate-email 400 tell the user to run `!!register` again — retrying the same token will only
   return `Invalid registration token`.
6. **Both TTLs are 15 minutes** (pre-reg token and link code) and the clock starts on the bot's DM,
   not on page load. Surface expiry in the UI.
7. **Post-registration linking is best-effort.** Group auto-join, GamePlayer back-fill and league
   resolution are all swallowed on error. Don't show "you're in your groups" based on a 201 — refetch
   `/api/users/:id/groups`.
8. **Two different error body shapes** (§4). Parse defensively.
9. **`termsVersion` is a magic string** duplicated in `src/app/auth/signup/page.tsx` and
   `src/app/register/page.tsx` as `"20250708"`. Any new client must send the same current value;
   consider centralizing it.
10. **Phone normalization is digit-stripping, not libphonenumber.** A bare 10-digit number is assumed
    US and gets `1` prepended; 11+ digits are stored as-is. Non-US users entering a national-format
    number will be stored wrong.

## 8. Post-creation onboarding gate (what a new client must reproduce)

After account creation the web client pushes the user toward WhatsApp linking before they can use
the app. This is a soft gate, not a hard block — read this section carefully before mimicking it,
because the enforcement is weaker than it looks.

### Where new accounts land

| Flow | Redirect after auto-sign-in | Why |
|------|-----------------------------|-----|
| A — web signup | `/connect-whatsapp` | account has no `WhatsAppIdentity` yet |
| B — WhatsApp registration | `/my-games` | already linked by the register endpoint, gate passes |
| Either, if auto-sign-in fails | `/auth/signin?registered=true` | account exists; user logs in manually |

### How the gate actually works

`useProtectedRoute` (`pickuppal-web/src/hooks/useAuth.tsx`) runs on every protected page render:

```
if (authenticated && pathname !== "/connect-whatsapp"):
    GET /api/users/:id/whatsapp-identity
    if (!identity.linked) router.push("/connect-whatsapp")
```

The one-time redirect from the signup form is not the gate — it exists only for the happy path. The
per-page check is what catches a user who closed the tab mid-onboarding or logged back in later.

### The four limits of that gate

1. **Client-side only.** It is a `useEffect` in a React hook. No server route, no Next.js
   `middleware.ts` involvement (`middleware.ts` only checks that a NextAuth token exists — it does not
   look at linked status).
2. **It covers three pages.** `useProtectedRoute` is used by `/my-games`, `/leagues`, `/admin/events`,
   and the shared `ProtectedRoute` component. `/marketplace`, `/game/[gameId]`, `/profile` and the
   rest are not gated.
3. **It fails open.** The identity lookup is `.catch(() => {})` — a network error or a bot-service
   outage lets the user straight through, deliberately.
4. **`/connect-whatsapp` has a "Skip for now" button** that routes to `/marketplace`, which is
   ungated. A user can decline linking and keep using the app.

The API enforces none of this. Every bot endpoint — joining games, creating games, leagues — works
normally for an account with zero `WhatsAppIdentity` rows. There is no server-side "unlinked" state
and no 403 anywhere.

So the accurate description of current behavior is: unlinked users are repeatedly nudged to the
linking screen from the three main app pages, and can route around it. If the app needs a genuine
block, that is new backend work, not parity work — see "Decisions for the app" below.

### What the `/connect-whatsapp` screen does

Two steps, the second optional:

**Step 1 — link the personal account**

- `POST /api/users/:id/link-code` → `{ code, expiresAt }`
- Display the 6-char code, a live countdown to `expiresAt` (15 min), and the bot number
  +1 (650) 220-5416; offer copy-to-clipboard and regenerate
- Poll `GET /api/users/:id/whatsapp-identity` every 5 s
- When `linked` is true, stop polling and advance to step 2

The user sends `!!link <code>` to the bot from WhatsApp. Nothing is posted back from the client —
linking completes entirely on the bot side, which is why the client polls.

**Step 2 — link a WhatsApp group (optional):** user enters a group linkage code, client previews the
group, then attaches it. Skippable via "Skip for now".

### Decisions for the app

Parity with web is cheap; a real gate is not. Pick deliberately:

1. **Same soft gate** — implement the identity check on your equivalent of the three main screens,
   keep fail-open and keep the skip. Zero backend work.
2. **Real block** — gate the app's root navigation on `linked`, remove the skip. Still client-side, so
   still bypassable by anyone calling the API directly, but honest for a normal user. Zero backend work.
3. **Enforced server-side** — requires new work on the bot: an "unlinked" concept and 403s on the
   endpoints that should be off-limits. Note this collides with gotcha #2 (the API is
   unauthenticated, so there is nothing to enforce against until bearer-token auth from
   `MOBILE_APP_PLAN.md` §6 lands).

Also note the term "verify" does not apply here. There is no email verification anywhere in the
product — no verification token, no confirm-email endpoint, no `emailVerified` column. Linking
WhatsApp proves control of a phone number; nothing proves control of the email address.

### Mobile-specific wrinkle

The web flow assumes the user can read a code on one screen and type it into WhatsApp on another
device. On a phone both apps are on the same device, so:

- Make the code tappable-to-copy, and consider a `https://wa.me/16502205416?text=!!link%20K7M2QX`
  deep link that opens WhatsApp with the command pre-filled.
- Keep polling while backgrounded, or re-check `whatsapp-identity` on app foreground — the user will
  leave your app to complete this step, and the 15-minute code TTL keeps running while they are away.

The alternative direction (flow B, `!!register` from WhatsApp) may be the better default on mobile: it
produces an account that is linked on arrival and never sees this gate at all.
