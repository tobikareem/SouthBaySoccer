# PROF-5 — Player profile screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, the seed-data strategy) lives in [`../../design.md`](../../design.md) — this screen
is built in the **UI-first phase** against a seed client (§12). The reusable UI contract is
[`../../client-ui.md`](../../client-ui.md); the visual source of truth is the `profile` screen in
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
This screen's Font Awesome contract implements `INV-13`.

The profile is an authenticated tab inside the signed-in Shell (Sessions / Stats / Profile). All
career figures are **derived-on-read** values returned by the client (`INV-7`); the screen never
computes or maintains stats and never edits the profile in-app.

## Screen composition

`ProfilePage` is a `BrandPage` with a fixed `BrandHeader`, a vertically scrollable content area, and
the Shell `TabBar` (Profile active). It composes only shared controls (`client-ui.md` §6) and tokens
(`client-ui.md` §4) — no page-local hex, font sizes, or emoji. Matching the wireframe order:

1. **`BrandHeader`** — green-to-pine header with a 54-dip `Avatar` (`Variant=OnGreen`, player
   initials/image), the player `Title` (name "Tobi Kareem"), and `Subtitle` (`"Captain" · #8`) supplied
   via `LeadingContent`/header binding. No back button (it is a root tab).
2. **Identity row** (`SpaceLg` content padding) — a `Badge` (`Variant=Success`, WhatsApp brands
   glyph) reading "Linked via WhatsApp" on the left, and a `LinkButton` "Edit on Pickup Pal" on the
   right (`OpenPickupPalEditCommand`, external-link glyph), space-between.
3. **`SectionHeader`** "Career stats".
4. **`StatTile` grid** — a three-column responsive grid (`SpaceMd` gutters) of six `StatTile`s bound
   from `CareerStats`: Matches, Goals, Assists, Avg rating, MVP, Likes. Each tile uses `TextStat`
   value + `TextCaption` label from `StatTileSurface`.
5. **"Recent form" `BrandCard`** — title "Recent form" (`TextBodyStrong`) with a muted "last 5"
   caption, and a horizontal run of result `Badge`s bound from `RecentForm`
   (`Win→Success`, `Draw→Warning`, `Loss→Danger`, with W/D/L text so meaning is not color-only).
6. **Pending note** — a muted `TextCaption` row with a Font Awesome clock glyph (`Warning` tint),
   bound to `PendingNote`; the whole row is collapsed when `PendingNote` is null/empty.
7. **`GhostButton`** "View season leaderboard" — full width (`OpenLeaderboardCommand`).

`StateView` wraps the scrollable content so loading / empty / error / offline render in place of the
profile body; the header and tab bar remain visible.

### Tokens

Colors, radii, typography, spacing, avatar sizes (`AvatarLg` 54), and touch sizes (`TouchMin` 44)
come from `BrandColors.xaml` / `BrandTokens.xaml` / `BrandStyles.xaml`. The stat grid uses `SpaceMd`
gutters and `SpaceLg` horizontal content padding.

## Font Awesome contract (`INV-13`)

Reuses the registered `FontAwesomeSolid` / `FontAwesomeBrands` families and the typed glyph catalog
(`Resources/Fonts/FontAwesomeGlyphs.cs`) established by AUTH-7 — no inline Unicode literals. Glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Linked-via-WhatsApp badge | Brands | `whatsapp` |
| Edit on Pickup Pal | Solid | `arrow-up-right-from-square` |
| Pending-confirmation note | Solid | `clock` |
| Tab bar (Sessions / Stats / Profile) | Solid | `calendar` / `trophy` / `user` |

Each pictogram carries a `SemanticProperties.Description`; Font Awesome is for pictograms only, body
copy stays on the brand typography.

## MVVM & navigation

`ProfilePage` → `ProfilePageModel`; page code-behind only calls `InitializeComponent`. The page model
exposes `BindableProperty`-bound state and `ICommand` actions (`CommunityToolkit.Mvvm`
`[ObservableProperty]` / `[RelayCommand]`); no business, eligibility, or navigation logic lives in
XAML code-behind.

`ProfilePageModel` depends on the seed `IProfileClient` (UI-first; later swapped for the typed API
client by DI with no page/page-model change — `../../design.md` §12), plus `IExternalLauncher` and
the Shell navigator.

State:

- `Profile` (identity: name, subtitle/captain + number, avatar source/initials);
- `CareerStats` (the six derived tile values);
- `RecentForm` (ordered last-five results);
- `PendingNote` (string?, drives note visibility);
- `IsBusy` and a `State` exposed to `StateView`.

Commands:

- `EditOnPickupPalCommand` — opens the external Pickup Pal account page via `IExternalLauncher`
  (no in-app edit; never mutates stats).
- `OpenLeaderboardCommand` — navigates to the Leaderboard route.
- `RefreshCommand` — (re)loads the profile from `IProfileClient`; bound to `StateView.RetryCommand`
  and invoked on appearing.

The external account URI is supplied through typed configuration, not embedded in XAML or the page
model. No personal data is placed in logs or URLs.

## Seed dependency

`IProfileClient` returns a deterministic fixture matching the `profile` wireframe (name "Tobi
Kareem", `"Captain" · #8`, career totals 24/12/9/7.8/3/41, recent form W/W/D/W/L, and the
"2 goals from Sat awaiting confirmation" pending note). A `SeedProfileClient` (in
`SouthBaySoccer/SeedData/`) provides it for this phase; it carries no real personal data and is
excluded from Release per `../../design.md` §12. A null/empty pending note exercises the hidden-note
path and a profile-absent fixture exercises the empty state.

## States

`initial/loading` (request in flight) · `content` (profile bound) · `empty` (no profile) ·
`recoverable error` (retryable via `RefreshCommand`) · `offline` (no connectivity, retryable). The
header and tab bar persist across all states.

## Test design (`Client.Tests`) — PROF-5 slice

- the page model loads identity, career stats, recent form, and the pending note from `IProfileClient`;
- the pending note is exposed only when the fixture reports unconfirmed activity, and hidden otherwise;
- `EditOnPickupPalCommand` invokes the external launcher with the configured account URI and does not
  modify stats or present an in-app edit form;
- `OpenLeaderboardCommand` navigates to the Leaderboard route;
- loading / empty / error / offline states are surfaced and `RefreshCommand` re-requests the profile;
- icon controls expose semantic descriptions and the page stays scrollable and uncut at large text
  sizes and the narrowest supported width.

Build `net10.0-windows10.0.19041.0`.
