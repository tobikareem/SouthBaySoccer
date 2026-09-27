# Pickup Pal — Mobile sign-up implementation guide (received 2026-09-16)

Source: "Mobile Sign-Up Implementation Guide" PDF from the Pickup Pal developer (their Option A,
branch `feat/mobile-app-register-redirect`, already merged on their side). Transcribed here as the
authoritative contract for the N9ja Bay `!!register` flow; the full endpoint catalogue is
`pickuppal-api.postman.json` in this folder.

## End-to-end sequence

1. User taps "Sign up with WhatsApp" in N9ja Bay.
2. App opens WhatsApp via deep link with the message prefilled:
   `https://wa.me/16502205416?text=%21%21register%20source%3Dn9jabay`
   (that is `!!register source=n9jabay`, URL-encoded). The user only taps Send.
3. Bot replies by DM with `https://n9jabay.app/register?token=<token>`.
4. Tapping the link opens the app through a Universal Link (iOS) / App Link (Android).
5. App hands the token to our Functions, which call
   `GET /api/users/register/whatsapp/validate?token=<token>` → `{ valid: true, phoneNumber: "+1..." }`.
   The phone prefills the native form read-only.
6. Native form: first name, last name, email, password, terms acceptance. No WhatsApp from here on.
7. Functions call `POST /api/users/register/whatsapp` with
   `{ token, firstName, lastName, email, password, termsVersion, termsAcceptedAt }`.
   Pickup Pal redeems the token, creates the User, writes the WhatsAppIdentity row. The account is
   born linked. Our Functions then establish the N9ja Bay session.

## What N9ja Bay must build (their list)

1. Register the Universal Link / App Link domain `n9jabay.app`: serve `apple-app-site-association`
   and Android `assetlinks.json`. Pickup Pal never serves this domain.
2. Build the WhatsApp deep link above. If WhatsApp is not installed the link opens nothing; show a
   fallback with the bot number.
3. Handle the incoming link, extract `token`, validate server-side.
4. The native sign-up form (the Apple-required piece).
5. Submit server-to-server into the existing register endpoint.

## Already done on Pickup Pal's side

- `!!register source=n9jabay` reroutes the reply link to a per-client URL instead of `WEB_APP_URL`.
  It reuses their existing attribution parser; no new command syntax, no schema changes, no new
  endpoints.
- New env var `CLIENT_REGISTER_URLS`, a JSON map of `source` key → base URL.
- **Still needed before go-live:** they set `CLIENT_REGISTER_URLS='{"n9jabay":"https://n9jabay.app"}'`
  once we confirm the real Universal Link domain.
- `validate`, `register/whatsapp`, and `auth` are untouched and identical for web and mobile.

## Returning-user login (their position)

The WhatsApp token is single-use and only for account creation. Their expectation for every later
sign-in is a normal credentials login:
`POST /api/users/auth { "login": "email-or-phone", "password": "..." }`, which already accepts a
phone as `login`.

**N9ja Bay note:** there is no `!!login` command and no login-token redemption endpoint. Our
verified sign-in flow therefore stays dormant (`Onboarding:RequireWhatsAppVerification=false`) and
phone lookup remains the production sign-in until Pickup Pal adds possession proof or we adopt
password login as a supplement. See `.ai/memory/pickuppal-account-creation.md`.

## Raw extracted text

<details>
<summary>PDF text as extracted (for reference)</summary>

```
--- page 1 ---
PICKUPPAL · INTERNAL
Implementation guide: mobile app sign-up via
WhatsApp
How Captain wires his app into pickupPal's existing !!register  flow (Option A). His work and
Will's work are called out separately below — Will's side is already merged on feat/mobile-app-
register-redirect .
01End-to-end sequence
Purple steps are Captain's app/backend. Green steps are pickupPal's bot/API, already built.
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 1/6

--- page 2 ---
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 2/6

--- page 3 ---
WhatsAppn9jabay app
User
Tap "Sign up with WhatsApp"
Deep link, prefilled:"!!register
source=n9jabay"
Tap Send
"!!register source
DM reply
n9jabay.app/regist
shows reply link
Tap link (Universal Link
reopens app)
token
prefill pho
fill name / email / password
submit for
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 3/6

--- page 4 ---
WhatsAppn9jabay app
session establ
User
Captain builds this Already live on pickupPal's side
02What Captain needs to build
Five pieces, in the order a user hits them.
1 Register a Universal Link (iOS) / App Link (Android) domain HIS APP
n9jabay.app  needs an apple-app-site-association  file (and Android's assetlinks.json )
so that tapping https://n9jabay.app/register?token=...  inside WhatsApp opens his native app
directly instead of a browser. This is entirely on his infrastructure — pickupPal never serves this
domain.
2 Build the WhatsApp deep link HIS APP
From the "Sign up with WhatsApp" button, open:
https://wa.me/16502205416?text=%21%21register%20source%3Dn9jabay
That's !!register source=n9jabay  URL-encoded. It opens WhatsApp with the bot's chat pre-
filled — the user only has to tap Send.
Note: if WhatsApp isn't installed, the link fails to open anything. Worth a fallback screen showing the
number to text manually.
3 Handle the incoming Universal Link + validate the token HIS APP
When the app reopens from the WhatsApp reply link, extract token  from the URL. Server-side
(Azure Functions), call:
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 4/6

--- page 5 ---
GET {WEB_APP_URL_EQUIVALENT}/api/users/register/whatsapp/validate?token=<token>
→ { valid: true, phoneNumber: "+1..." }
Same endpoint the web app already uses — unchanged, no new access needed. Use the returned
phone number to prefill (read-only) the native form.
4 Native sign-up form HIS APP
This is the actual Apple-required piece — name, email, password, terms acceptance, fully in-app. No
WhatsApp involved from here on.
5 Submit — server to server, into the existing endpoint HIS APP
Azure Functions calls the same endpoint the web app already calls, unchanged:
POST /api/users/register/whatsapp
{
  "token": "...",
  "firstName": "...",
  "lastName": "...",
  "email": "...",
  "password": "...",
  "termsVersion": "...",
  "termsAcceptedAt": "..."
}
pickupPal redeems the token, creates the User , and writes the WhatsAppIdentity  row — the
account is born already linked. Captain's Functions layer then establishes his own app session; that
part is entirely his auth stack.
03Already done on pickupPal's side
Merged on feat/mobile-app-register-redirect  in pickuppal-bot . No schema changes, no new
endpoints — one new env var and a few lines in the existing !!register  handler.
!!register source=n9jabay  now reroutes the reply link to a per-client URL instead of always
WEB_APP_URL  — reuses the existing attribution parser, no new command syntax.
New env var CLIENT_REGISTER_URLS  — JSON map of source  key → base URL.
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 5/6

--- page 6 ---
Every other endpoint (validate , register/whatsapp , auth ) is untouched — identical for web and
mobile.
Still needed before this goes live: Will needs to set
CLIENT_REGISTER_URLS='{"n9jabay":"https://n9jabay.app"}'  in the deployment environment
once Captain confirms his app's real Universal Link domain.
04Returning-user login (already exists, no new work)
The WhatsApp token is single-use, only for account creation — it's never checked again. Every later app
open is a normal credentials login:
POST /api/users/auth
{ "login": "email-or-phone", "password": "..." }
This endpoint already exists and already accepts phone as the login  value — nothing new needed for day-
to-day app opens.
Generated for Captain's mobile implementation · pickupPal
9/16/26, 12:10 PM Mobile Sign-Up Implementation Guide
https://regal-kitten-p3ep.here.now 6/6
```

</details>
