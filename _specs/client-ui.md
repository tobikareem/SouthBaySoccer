# SouthBaySoccer — MAUI Client Reusable UI Spec (Design System)

Specification for a reusable UI layer in the .NET MAUI client: **design tokens** (colors, fonts,
text styles, spacing), **shared styles**, and **custom XAML controls**. Grounded in
[`skills/brand-design-kit`](../skills/brand-design-kit/SKILL.md), the
[mobile wireframes](../documentation/mobile-wireframes.html), and the client architecture in
[`documentation/architecture.md`](../documentation/architecture.md) §6.

> **Visual authority:** `documentation/mobile-wireframes.html` is the source of truth for mobile
> screen composition, hierarchy, component appearance, and interaction states. This document is
> the MAUI implementation contract for that wireframe. If they diverge, update this spec and the
> shared control library to match the wireframe before implementing product pages.

> **Status: complete.** The reusable MAUI UI foundation is implemented: brand resources, shared
> styles, the eleven-control catalog, Shell theming, and the navigable UI Library showcase are
> present in the client. Product-screen adoption is tracked separately by the relevant M11 story
> tasks and does not reopen this design-system specification. The first product-screen wave requires
> the additive `M11.0c` extensions in §6.1; these extend the shared library and must land before a
> page attempts a local substitute.

## 1. Purpose & principles

A single, token-driven UI vocabulary so every screen looks like the wireframes and the brand,
with zero per-page styling drift.

- **Token-driven** — pages and controls reference named resources (colors, sizes, text styles). No raw hex, font sizes, or magic numbers in pages.
- **MVVM-clean** — controls expose `BindableProperty` inputs and `ICommand` outputs; no business logic in control code-behind (architecture §6). Controls render and raise events only — they never decide eligibility/payment/auth.
- **Reuse over duplication** — prefer a shared style or control over copy-pasted XAML.
- **Theme-aware** — every color is defined for light and dark via `AppThemeBinding`.
- **Accessible** — semantic descriptions, ≥44px touch targets, sufficient contrast, dynamic type.
- **Brand-first** — green/white Nigerian-flag identity, white-dominant (60/30/10), Flag Green `#008751`.
- **Wireframe-first** — product pages compose the exact patterns demonstrated in
  `documentation/mobile-wireframes.html`; page-local alternatives require a wireframe update first.

## 2. Current state

The client retains the MAUI template resources for unmigrated sample screens. The brand resources
are merged after them in `App.xaml`, and the reusable catalog lives under `Controls/`. Legacy
sample controls remain under `Pages/Controls/` and are migrated or retired only when their product
feature is replaced; do not wholesale-rewrite the sample app.

## 3. Implemented file and resource layout

```
SouthBaySoccer/
├── Resources/Styles/
│   ├── Colors.xaml          (existing — template)
│   ├── Styles.xaml          (existing — template)
│   ├── AppStyles.xaml       (existing)
│   ├── BrandColors.xaml     (brand color + brush tokens, light/dark)
│   ├── BrandTokens.xaml     (spacing, radii, sizes, font sizes as x:Double/x:String)
│   └── BrandStyles.xaml     (typography + control styles keyed/implicit)
└── Controls/                (reusable ContentView controls + code-behind)
    ├── BrandHeader.xaml(.cs)
    ├── BrandCard.xaml(.cs)
    ├── Badge.xaml(.cs)
    ├── Avatar.xaml(.cs)
    ├── StatTile.xaml(.cs)
    ├── CapacityBar.xaml(.cs)
    ├── SectionHeader.xaml(.cs)
    ├── PlayerRow.xaml(.cs)
    ├── SegmentedControl.xaml(.cs)
    ├── CounterStepper.xaml(.cs)
    └── StateView.xaml(.cs)   (loading / empty / error / offline)
```

`App.xaml` merge order (brand **after** template so it wins):

```xml
<ResourceDictionary Source="Resources/Styles/Colors.xaml" />
<ResourceDictionary Source="Resources/Styles/Styles.xaml" />
<ResourceDictionary Source="Resources/Styles/AppStyles.xaml" />
<ResourceDictionary Source="Resources/Styles/BrandColors.xaml" />
<ResourceDictionary Source="Resources/Styles/BrandTokens.xaml" />
<ResourceDictionary Source="Resources/Styles/BrandStyles.xaml" />
```

Controls namespace: `SouthBaySoccer.Controls`; in XAML
`xmlns:c="clr-namespace:SouthBaySoccer.Controls"`.

## 4. Design tokens

### 4.1 Color tokens (`BrandColors.xaml`)

| Key | Light | Dark | Role |
|-----|-------|------|------|
| `BrandGreen` | `#008751` | `#1FB573` | Primary actions, headers, accents |
| `BrandGreenDark` | `#005C37` | `#0C7A4E` | Pressed/active, dark sections |
| `BrandSpring` | `#1FB573` | `#52D39A` | Highlights, progress fill |
| `BrandMist` | `#E8F5EE` | `#163A2B` | Tint surfaces, tiles, zebra, bars track |
| `BrandInk` | `#14241B` | `#ECF3EE` | Primary text |
| `BrandSage` | `#5B6B62` | `#9DB0A6` | Secondary/muted text |
| `BrandLine` | `#E7EBE7` | `#2A332E` | Borders/dividers (1px) |
| `Surface` | `#FFFFFF` | `#10160F` | Card/page surface |
| `SurfaceAlt` | `#FBFCFB` | `#171F18` | Inputs, subtle fills |
| `Success` | `#1FB573` | `#52D39A` | Positive status |
| `Warning` | `#BA7517` | `#E0A33A` | Caution (waitlist, deadline) |
| `Danger` | `#A32D2D` | `#E26B6B` | Full/error/loss |
| `WhatsApp` | `#25D366` | `#25D366` | Pickup Pal / WhatsApp-branded external actions only |

Each is an `AppThemeBinding`-backed `Color`; provide a matching `SolidColorBrush` (`…Brush`) where a brush is needed. Keep the template `Primary`/`Gray*` keys for unmigrated template screens.

### 4.2 Typography (`BrandTokens.xaml` sizes + `BrandStyles.xaml` styles)

Header font Inter Semibold; body Inter Regular (registered in `MauiProgram.cs` from Google Fonts). Weights: Regular + Semibold for core UI, with Bold available for native platform fallback and future display needs.

| Style key | Size | Weight | Use |
|-----------|------|--------|-----|
| `TextDisplay` | 28 | Semibold | Screen hero / big number |
| `TextH1` | 21 | Semibold | Screen title |
| `TextH2` | 17 | Semibold | Card/section title |
| `TextBody` | 15 | Regular | Body |
| `TextBodyStrong` | 15 | Semibold | Emphasis |
| `TextCaption` | 13 | Regular | Secondary (`BrandSage`) |
| `TextLabel` | 11 | Semibold | Uppercase section labels (`BrandSage`, letter-spacing) |
| `TextStat` | 22 | Semibold | Stat-tile value (`BrandGreenDark`) |

### 4.3 Spacing, radius, sizing (`BrandTokens.xaml`)

- Spacing (`x:Double`): `SpaceXs` 4, `SpaceSm` 8, `SpaceMd` 12, `SpaceLg` 16, `SpaceXl` 24, `Space2Xl` 32.
- Radius: `RadiusSm` 8, `RadiusSegment` 9, `RadiusMd` 12, `RadiusAction` 14,
  `RadiusLg` 16, `RadiusPill` 999.
- Sizes: `TouchMin` 44; primary buttons 46 minimum; `AvatarSm` 28, `AvatarMd` 34,
  `AvatarLg` 54; `BarHeight` 8; `IconMd` 20.
- Wireframe padding: cards 15, badges `9,5`, buttons `14,11`, rows `0,8`, segments `12,7`.
- Layout: `LayoutPadding` (OnIdiom `12`/desktop `20`) is the page gutter; `IconSize` (21) and the
  five Shell tab icons (`IconSessions`, `IconGameDay`, `IconStats`, `IconPlayers`, `IconProfile`)
  also live in `BrandTokens.xaml` — migrated from the deleted sample-template dictionaries; the
  brand system owns them now.

## 5. Shared styles (`BrandStyles.xaml`)

Keyed styles (and a few implicit) built only from tokens:

- **Pages**: `BrandPage` (ContentPage) — `BackgroundColor=Surface`, sensible padding.
- **Labels**: the `Text*` styles in §4.2 (implicit `Label` = `TextBody`).
- **Buttons**:
  - `PrimaryButton` — `BrandGreen` bg, white text, `RadiusMd`, height ≥ `TouchMin`, pressed → `BrandGreenDark` (VisualState).
  - `GhostButton` — `Surface` bg, `BrandGreen` text + 1.5px `BrandGreen` border.
  - `WhatsAppButton` — `WhatsApp` bg, white text, leading WhatsApp glyph.
  - `DangerButton`, `LinkButton` (text-style).
  - `IconButton` — square/pill icon action, minimum `TouchMin`, neutral/green visual states.
  - `IconToggleButton` — reusable off/on states for like/MVP actions; state is bound, not decided in
    code-behind.
  - `HeroInverseButton` — white bg + `BrandGreen` text; the primary action inside a green hero card
    (Game Day check-in), where a green button would vanish. Use this, never inline
    `BackgroundColor="White"` on a page.
- **Entry/Editor**: `BrandEntry` — `SurfaceAlt` bg, `BrandLine` border, `RadiusMd`, focus ring `BrandGreen`.
- **Inputs**: `BrandEditor`, `BrandPicker`, `BrandDatePicker`, `BrandTimePicker` — same recipe as
  `BrandEntry` (SurfaceAlt bg, Ink text, Sage placeholder/title, Inter `FontBody`, `TouchMin`
  height). Every Editor/Picker/DatePicker/TimePicker on a product page must carry one of these;
  a bare input falls back to platform defaults, not brand type.
- **Baseline implicit styles** (owned here since the template dictionaries were deleted):
  `Shell` (+derived, brand tab-bar colors), `Page` (+derived, surface bg + zero padding),
  `CheckBox` (brand green, `TouchMin` minimums), `Switch` (brand green on-color),
  `ActivityIndicator` (brand green).
- **Frame/Border**: `CardSurface` (white, 1px `BrandLine`, `RadiusLg`), `TintSurface` (`BrandMist`).
- **Wireframe surfaces**: `HeroCardSurface` (Pine→Flag Green), `StatTileSurface`
  (Mist/subtle white, fine green-tinted line), `NoticeSurface` (Mist + green-tinted line),
  `IconTileSurface`, `MetadataChip`, and `StepperButton`.
- **Slider**: `RatingSlider` — tokenized track/thumb/focus treatment for the 0–10 teammate rating.

## 6. Custom control catalog

All are `ContentView` subclasses (except where noted) with `BindableProperty` inputs and `ICommand`
outputs. Each control: defines `VisualStateManager` states where interactive, sets
`SemanticProperties`, and uses only tokens. Below: bindable API + intent. (Defaults in parentheses.)

### BrandHeader
Green-to-pine app header with optional back button, title, subtitle, right flag stripe, and subtle
decorative motif.
- `Title` (string), `Subtitle` (string?), `ShowBack` (bool=false), `BackCommand` (ICommand?),
  `LeadingContent` (View?), `TrailingContent` (View?).
- A11y: back button `SemanticProperties.Description="Back"`, ≥44px.
- Wireframe: session/match-stats/rate headers.

### BrandCard
White surface container.
- `Body` (View, content property), `Padding` (Thickness=`CardPadding`), `IsTinted` (bool=false → uses
  `BrandMist`), `IsHero` (bool=false → Pine-to-Flag-Green surface).
- Wireframe: all cards.

### Badge (status pill)
- `Text` (string), `Variant` (enum `Neutral|Success|Warning|Danger`=Neutral), `Glyph` (string?).
- Maps Variant → token pair (e.g. Success → `BrandMist`/`BrandGreenDark`; Warning → warn bg/text). `RadiusPill`.
- Wireframe: "Going", "Full", "guest", "Paid".
- Game Day header mapping (via DataTriggers, never a hardcoded variant):
  `Open`/`CheckedIn` → Success, `Closed` → Neutral, `Blocked` → Warning, spectator → Neutral
  ("Spectator"). Spectator mode also renders the `NoticeSurface` explanation banner
  ("You're a member of {group} — you're not on this game's list…") above read-only StatTiles and a
  single Join CTA (`BrandCard IsTinted` + `CapacityBar` + `PrimaryButton`).

### Avatar
- `Initials` (string), `ImageSource` (ImageSource?), `Size` (double=`AvatarMd`), `Variant` (enum `Mist|OnGreen`=Mist).
- Circular; shows image if set else initials. A11y: description = player name.
- Wireframe: profile, rosters, leaderboard.

### StatTile
- `Value` (string), `Label` (string), `Glyph` (string?).
- `BrandMist` tile, `TextStat` value, `TextCaption` label. Used in responsive grids.
- Wireframe: profile career stats.

### CapacityBar
- `Current` (int), `Max` (int), `ShowLabel` (bool=true), `WarnThreshold` (double=1.0 → full uses `Danger`).
- Renders `BrandMist` track + `BrandGreen` fill `= Current/Max`; label "16 / 20 going".
- Wireframe: session capacity.

### SectionHeader
- `Text` (string, uppercase via style), `ActionText` (string?), `ActionCommand` (ICommand?),
  `ActionGlyph` (string?), `SecondaryActionText` (string?), `SecondaryActionCommand` (ICommand?),
  `SecondaryActionGlyph` (string?).
- A section may carry **two** peer actions, laid out inline on the header row. The secondary action
  is hidden unless its text is set, so single-action headers are unaffected.
- Glyphs are optional and ride on each button's `ImageSource`, never prepended to `Text` — the icon
  needs the icon font while the label needs the brand text font. Assign the `FontImageSource` in
  code, not via a XAML binding: an empty `Glyph` still reserves image space and would put a stray
  gap in front of the label on every glyph-less header.
- Wireframe: "Coming up", "Going · 16", "Confirm teammates · captain",
  "Admin · Broadcast / + Session" (two actions with glyphs).

### PlayerRow
- `LeadingContent` (View?), `Initials`/`ImageSource`, `Name` (string), `Detail` (string?),
  `TrailingText` (string?), `TrailingContent` (View?), `TapCommand` (ICommand?),
  `SemanticDescription` (string?, defaults to `Name`), `Glyph` (string?) + `GlyphFontFamily`
  (default `FontAwesomeSolid`).
- Avatar + name + subtitle + trailing; tappable. A11y: row description.
- **Menu mode**: setting `Glyph` swaps the person avatar for an `IconTileSurface` icon tile —
  use this for navigation/action rows (Game Day actions, recent-game rows). Never fake a person
  avatar with made-up initials for a non-person row; leave `Glyph` unset for real people.
- Wireframe: going/waitlist lists, confirm-teammates rows, leaderboard rows.
- The Game Day last-game team popup uses `Detail` for compact approved tallies: repeat `⚽` once per
  goal and `🦶` once per assist; prefix `Captain · ` for the captain. The last-game "Finish up this
  game" section includes the existing Rate teammates route for eligible participating players.

### SegmentedControl
- `ItemsSource` (IEnumerable), `DisplayMember` (string?), `SelectedIndex` (int, two-way), `SelectedItem` (object, two-way), `SelectionChangedCommand` (ICommand?).
- Pill segments on `BrandMist`; selected segment = `Surface`/`BrandGreen`.
- Wireframe: leaderboard Goals/Assists/Rating/MVP.
- Player Game Day uses a non-admin `Today | Recent games` segment when a current game and history
  both exist. `Today` remains the default and retains the live Game Day context. `Recent games`
  lists at most the player's three newest attended games, newest first, and reuses the existing
  summary, team-sheet popup, and eligible `Finish up this game` actions for the selected game.
  Group membership by itself does not add an unattended game to this history. The admin
  `My games | All games today` scope remains separate and applies only to the Today view.

### CounterStepper
- `Value` (int, two-way), `Minimum` (int=0), `Maximum` (int=99), `Step` (int=1), `Glyph` (string?), `Caption` (string?).
- Glyph/caption at left and − value + controls at right, with rounded-square 44px buttons. A11y:
  increment/decrement descriptions and disabled boundary states.
- Wireframe: match-stats goals/assists entry.

### StateView (loading / empty / error / offline)
- `State` (enum `Loading|Empty|Error|Offline|Content`=Content), `Title`, `Message`, `Glyph`,
  `GlyphFontFamily` (string, defaults to body font), `RetryCommand` (ICommand?), `Body` (View,
  content property — shown when `Content`).
- Standardizes the loading/empty/populated/error/offline states required by architecture §6.

### (Styles, not controls)
PrimaryButton / GhostButton / WhatsAppButton are **styles** (§5), applied to MAUI `Button`. Bottom
navigation uses Shell `TabBar` themed via `BrandStyles`, not a custom control.

## 6.1 First-wave additive extensions (`M11.0c`)

Before SES-6, PROF-5, LEAD-4, or STAT-8 is implemented:

- add `LeadingContent` to `BrandHeader` for the Profile avatar;
- add `LeadingContent` to `PlayerRow` for leaderboard rank/trophy content;
- add `IconButton`, `IconToggleButton`, `MetadataChip`, and `RatingSlider` shared styles;
- add each extension to the UI Library showcase and its accessibility tests.

Pages may use standard MAUI layout primitives (`Grid`, `VerticalStackLayout`, `CollectionView`) to
compose shared controls. Any repeated visual/interactive pattern must come from this catalog or a
named shared style; no page-local control template, raw style, or bespoke visual state is allowed.

## 7. Theming & dark mode

Every color token is `AppThemeBinding` (§4.1); controls never hard-code a color. A screen rendered
on a near-black background must keep all text legible. Provide both light and dark stops; verify
contrast (WCAG AA for text). The app may default to light but must not break in dark.

## 8. Accessibility

- `SemanticProperties.Description`/`Hint`/`HeadingLevel` on interactive and heading elements.
- Touch targets ≥ `TouchMin` (44).
- Respect OS dynamic type — sizes are tokens; avoid fixed heights that clip scaled text.
- Don't encode meaning by color alone (pair status color with text/glyph).
- Keyboard/focus order on Windows/Mac Catalyst.

## 9. Naming conventions

- Color keys: `Brand*`, semantic `Success/Warning/Danger`, `Surface*`, `WhatsApp`. Brush variant suffix `Brush`.
- Text styles: `Text*`. Spacing `Space*`, radius `Radius*`, sizes descriptive (`AvatarMd`).
- Controls: PascalCase nouns in `SouthBaySoccer.Controls`; bindable props PascalCase; commands `*Command`.
- One control per `.xaml`+`.xaml.cs`; file name = type name.

## 10. Acceptance criteria

The reusable-foundation acceptance criteria below are complete. They remain regression requirements
for future control and product-screen changes.

```gherkin
Scenario: Pages use tokens, not raw values
  Given any page or control XAML
  Then it references named color/text/spacing resources
  And it contains no inline hex colors, font sizes, or magic-number spacing

Scenario: Tokens resolve in both themes
  Given the app runs in light mode and in dark mode
  When any brand control is shown
  Then all text and surfaces use the theme-correct token and remain legible

Scenario: Controls are MVVM-friendly and logic-free
  Given a custom control
  Then its inputs are BindableProperty and its actions are ICommand
  And its code-behind contains no business or eligibility logic

Scenario: Controls are accessible
  Given an interactive control
  Then it exposes SemanticProperties and a touch target of at least 44px

Scenario: A new screen reuses the library
  Given a new screen built from this system
  Then it composes the applicable controls and named shared styles from this specification
  And it adds no bespoke one-off styles for those patterns

Scenario: Product UI follows the authoritative wireframe
  Given a new or changed MAUI product screen
  When its layout and interaction states are reviewed
  Then they match documentation/mobile-wireframes.html
  And any intentional design change updates the wireframe and this spec in the same change
```

## 11. Wireframe → control mapping

| Wireframe element | Control / style |
|-------------------|-----------------|
| Green screen header + back | `BrandHeader` |
| White cards | `BrandCard` |
| Going/Full/guest/dues pills | `Badge` |
| Avatars & initials | `Avatar` |
| Career stat tiles | `StatTile` (grid) |
| Capacity "16/20" bar | `CapacityBar` |
| "Coming up", "Going · 16" labels | `SectionHeader` + `TextLabel` |
| Going / waitlist / confirm rows | `PlayerRow` |
| Leaderboard Goals/Assists/Rating/MVP | `SegmentedControl` |
| Goals/assists +/- entry | `CounterStepper` |
| RSVP / Submit / WhatsApp buttons | `PrimaryButton` / `GhostButton` / `WhatsAppButton` |
| Loading / empty / error / offline | `StateView` |
| Group selection (sign-in) & Stats group filter | `LinkGroupPage` (single-select `CollectionView`) / `Picker` |
| Admin entry points on Sessions | `SectionHeader` with two actions ("Broadcast", "+ Session"), gated by `CanManageSessions` |
| Admin broadcast composer | `BrandHeader` + fixed-audience `MetadataChip` + styled `Editor` + `AnnouncementCard` preview + `ToggleRow` + `PushPreview` + docked `PrimaryButton` |
| Push notification preview | `PushPreview` (dark surface, app name, group title, 2-line clamped body) |
| Admin "Recently sent" read receipts | `BrandCard` + compact rows + `Badge` (`ok` / `warn` by read ratio) |
| Player group announcements | `BrandHeader` + `SegmentedControl` (All / Unread) + `AnnouncementCard` feed + `StateView` empty state |

## 11.1 Admin broadcasts and player notifications

Group announcements use two connected client surfaces. Both are built from one shared
`AnnouncementCard` template — the admin preview and the player feed row are the same control, so the
composer literally shows what players will receive.

- **Admin broadcast composer.** An admin-only page in four ordered blocks: *audience → message →
  how it lands → send*.
  - **Audience** is stated, not chosen: a single read-only chip showing the admin's own group chat
    and its member count. An admin broadcasts to the group they run and nowhere else, so there is
    nothing to select between, and a picker implied a reach they do not have. Resolve it from the
    player's primary group link, falling back to their only linked group when no primary is flagged.
    The chip is presentation, not the security boundary — `PostAnnouncementCommandHandler` already
    rejects a post to a group the caller is not linked to, and that check stays authoritative.
    With no audience to change, the preview, push preview, and CTA recipient count are fixed for the
    session; the empty state ("Link a group chat before you can broadcast") covers an unlinked admin.
  - **Message** is a styled `Editor` with the character counter sitting on the label line
    (`104 / 500`), programmatically associated with the editor.
  - **How it lands** shows the `AnnouncementCard` preview, a `ToggleRow` switch for the push
    notification, and — only while the toggle is on — a `PushPreview` rendering the OS-level
    notification. Toggling push must show/hide that preview.
  - **Send** is a docked `PrimaryButton` pinned to the bottom of the scroll (`sticky`-equivalent:
    a bottom-anchored row over a fade), labelled with the exact recipient count
    ("Broadcast to 24 members"), with the "cannot be edited after sending" caption beneath it.
  - A **Recently sent** list gives admins the read receipts (`24/24`, `14/18` badges — `ok` at full
    read, `warn` below). Read counts are admin-facing only.
  - The composer states that delivery is in-app, not WhatsApp.
- **Player group announcements.** The notification bell on Sessions opens a read-only, group-scoped
  feed. It uses cards on a plain surface rather than chat bubbles: sender + group on the left, time
  on the right, an unread dot for new items, the message body at readable size, and a divided footer
  only when the announcement carries context (a `meta-chip` and a link such as `View session`).
  - An **All / Unread** `SegmentedControl` filters the feed; the Unread tab carries the count.
  - Announcements are grouped under quiet **Today / Earlier** day labels.
  - **Mark all read** clears the unread cards, the bell dot, and the unread count together.
  - When no announcement matches the filter, `StateView`'s empty state renders "You're all caught up."
  - Players never see read counts — that data belongs to the admin surface.

Broadcast submission validates a non-empty message at the Application/PageModel boundary and exposes
an inline accessible error. While sending, the CTA is disabled and the operation uses an idempotency
key. Success locks the audience, message, and push option; retryable failure or offline state preserves
the draft and offers Retry through `StateView`. The visible label, character counter, and validation
message are semantically associated with the editor.

The feed reads as a calm announcement list, not member-to-member chat and not WhatsApp delivery.
Use the standard `Editor`, `BrandCard`, badge, avatar, and button styles; the audience row, toggle
row, push preview, and announcement card are shared controls, never page-local XAML. The audience
row is a `radiogroup` and the push row a `switch` for accessibility, and the unread dot is paired
with an accessible unread-count description on the bell.

## 11.2 Group-chat linking & group-scoped leaderboard

Players belong to WhatsApp group chats (mirrored from the read-only PickupPal API into our own
database — see backend spec). Two client surfaces implement this:

- **`LinkGroupPage` (route `//link-group`, blocking).** Shown immediately after sign-in when the
  player is linked to no group (`AuthenticationNavigator` gates the initial route on
  `IGroupsClient.GetMyGroupsAsync().IsLinked`). It is a **required** step: declared as a
  `ShellContent` outside the `TabBar` (no tab, no back stack), `Shell.NavBarIsVisible="False"`,
  `Shell.TabBarIsVisible="False"`, and `OnBackButtonPressed` returns `true`. Layout reuses
  `StateView` + a single-select `CollectionView` of `CardSurface` rows (VSM `Selected` state tints
  the row) + a `PrimaryButton` disabled until a group is picked. On link it routes to `//sessions`
  via `IGroupLinkNavigator`.
- **Stats leaderboard group filter.** The former season chevron badge is replaced by a `Picker`
  bound to the player's linked groups plus an "All groups" aggregate, defaulting to the player's
  **primary** group. The selected group id is threaded to `stats/leaderboards?groupId=…`; the top-5
  is scoped to that group's members (membership-based, not game-tagged).

## 12. Out of scope / dependencies

- Business behavior (RSVP rules, eligibility, stat confirmation) lives in PageModels/services, not controls.
- Navigation is Shell (architecture §6); this spec styles it but does not replace it.
- Product-screen adoption remains part of milestone **M11** and is tracked by its own story tasks.
  The completed reusable foundation must be used by all new client screens.
