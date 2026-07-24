# AUTH-7 — Welcome Back screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, persistence, auth/token flow) lives in [`../../design.md`](../../design.md); the
reusable UI contract is [`../../client-ui.md`](../../client-ui.md); the visual source of truth is
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html)
(first `signin` screen). This screen's Font Awesome contract implements `INV-13`.

The screen is shown only when no valid authenticated session can be restored. It is an
authentication route **outside** the signed-in Shell; the flyout and Sessions/Stats/Profile tabs
must not appear behind it (`INV-11`, fail-closed).

## Screen composition

`WelcomeBackPage` uses a vertically scrollable layout (matches the wireframe order/hierarchy):

1. Green-to-Pine brand header (~`34,16,30` padding): 42-dip circular football mark, `N9ja Bay`
   title, `Pickup soccer, organized.` subtitle, white right-side flag stripe + low-opacity circular motif.
2. Content area, 16-dip horizontal / 20-dip top padding: `WELCOME BACK` (`TextLabel`),
   `Your next game starts here.` (`TextH1`), Pickup Pal explanatory copy (`TextCaption`).
3. Phone-number input surface: Font Awesome phone glyph + phone input (telephone keyboard,
   international-format validation). The wireframe number `+1 (516) 344-7233` is **sample data only**;
   production uses the last entered number or an example placeholder — never a personal number default.
4. Full-width `Sign in with phone` action (primary brand treatment) → behavior in `AUTH-8`.
5. `NoticeSurface` with shield-check glyph + exact security copy:
   `Password-free and secure` then
   `Pickup Pal verifies your account. N9ja Bay stores only app session tokens on this device.`
6. Pickup Pal bot `BrandCard` (`SurfaceAlt`): `PICKUP PAL BOT` label, configured number, external-link
   glyph + `Open`, `Need help? Open the Pickup Pal bot for account support.` → behavior in `AUTH-9`.
7. Divider with centered `not on pickup pal?`.
8. Full-width `Sign up on Pickup Pal` ghost action (external-link glyph) → behavior in `AUTH-9`.
9. Centered caption: `Create your account on the web, then come back and sign in with your phone number.`

Colors, radii, typography, and touch sizes come from `BrandColors.xaml`/`BrandTokens.xaml`/
`BrandStyles.xaml`; the page adds no raw hex colors or emoji.

## Font Awesome contract (`INV-13`)

Free desktop font files committed as MAUI font resources and registered in `MauiProgram.cs`:

```text
SouthBaySoccer/Resources/Fonts/Font Awesome 6 Free-Solid-900.otf  -> "FontAwesomeSolid"
SouthBaySoccer/Resources/Fonts/Font Awesome 6 Brands-Regular-400.otf -> "FontAwesomeBrands"
```

A typed glyph catalog (`Resources/Fonts/FontAwesomeGlyphs.cs`) is referenced by XAML — no inline
Unicode literals. Required glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Product mark | Solid | `futbol` |
| Phone field/action | Solid | `phone` |
| Security notice | Solid | `shield-halved` / `shield` |
| External actions | Solid | `arrow-up-right-from-square` |

Font Awesome is for pictograms only; body copy stays on Open Sans / Segoe Semibold. The Font Awesome
Free license/attribution file must remain in the repo.

## MVVM, startup routing & security

`WelcomeBackPage` → `WelcomeBackPageModel` (page code-behind only calls `InitializeComponent`).
Commands and external/URI/validation/sign-in logic live in the page model (see `AUTH-8`/`AUTH-9` for
the command behaviors). Startup coordinator: check secure storage for a refresh token → attempt one
safe refresh → route to authenticated Sessions Shell on success → otherwise show `WelcomeBackPage`
and clear invalid credentials.

States: initial - invalid phone - signing in - account not found - offline - recoverable service error. Never log the full phone number, access token, or refresh token; mask in telemetry. Authentication is established only by a successful Pickup Pal phone lookup followed by SouthBaySoccer token issuance - never by button navigation or browser return.

## Test design (`Client.Tests`) — AUTH-7 slice

- signed-out startup selects `WelcomeBackPage`; a valid restored session bypasses it;
- wireframe copy is exposed by the page model;
- icon controls expose semantic descriptions;
- the page remains scrollable and uncut at large text sizes and the narrowest supported width.

(Phone sign-in and external-launch test cases are specified in `AUTH-8`/`AUTH-9`.)



