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
| `WhatsApp` | `#25D366` | `#25D366` | WhatsApp SSO button only |

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
- **Entry/Editor**: `BrandEntry` — `SurfaceAlt` bg, `BrandLine` border, `RadiusMd`, focus ring `BrandGreen`.
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
- `Text` (string, uppercase via style), `ActionText` (string?), `ActionCommand` (ICommand?).
- Wireframe: "Coming up", "Going · 16", "Confirm teammates · captain".

### PlayerRow
- `LeadingContent` (View?), `Initials`/`ImageSource`, `Name` (string), `Detail` (string?),
  `TrailingText` (string?), `TrailingContent` (View?), `TapCommand` (ICommand?).
- Avatar + name + subtitle + trailing; tappable. A11y: row description.
- Wireframe: going/waitlist lists, confirm-teammates rows, leaderboard rows.

### SegmentedControl
- `ItemsSource` (IEnumerable), `DisplayMember` (string?), `SelectedIndex` (int, two-way), `SelectedItem` (object, two-way), `SelectionChangedCommand` (ICommand?).
- Pill segments on `BrandMist`; selected segment = `Surface`/`BrandGreen`.
- Wireframe: leaderboard Goals/Assists/Rating/MVP.

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

## 12. Out of scope / dependencies

- Business behavior (RSVP rules, eligibility, stat confirmation) lives in PageModels/services, not controls.
- Navigation is Shell (architecture §6); this spec styles it but does not replace it.
- Product-screen adoption remains part of milestone **M11** and is tracked by its own story tasks.
  The completed reusable foundation must be used by all new client screens.
