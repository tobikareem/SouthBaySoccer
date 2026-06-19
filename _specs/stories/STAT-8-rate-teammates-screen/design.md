# STAT-8 — Rate teammates screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, persistence, seed-data strategy) lives in [`../../design.md`](../../design.md); the
reusable UI contract is [`../../client-ui.md`](../../client-ui.md); the visual source of truth is the
`rate` screen in
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
This screen's Font Awesome contract implements `INV-13`.

The screen captures peer ratings (`STAT-3`/`INV-8`), likes (`STAT-4`), and the single MVP award
(`STAT-5`) for one match. In this UI-first phase it binds to a **seed** `IStatsClient`, swapped for
the typed API client at M11.1 with no page or page-model change ([`../../design.md`](../../design.md)
§12). The page renders and raises events only; the no-self-vote, one-vote-per-peer, one-like, and
single-MVP rules are enforced in the page model — never in control code-behind. Server-side
enforcement of the same invariants is re-verified when the STAT backend milestone lands.

## Screen composition

`RateTeammatesPage` is a `BrandPage` with a vertically scrollable list of teammate cards above a
`PrimaryButton`, composed entirely from the reusable control catalog
([`../../client-ui.md`](../../client-ui.md) §6) and brand tokens — no page-local hex, font sizes, or
emoji. It matches the wireframe order/hierarchy:

1. **`BrandHeader`** — `ShowBack=true` with `BackCommand`, `Title` `Rate the match`, `Subtitle`
   `Sat · Marina Field`. The back affordance carries `SemanticProperties.Description="Back"` and is
   ≥ `TouchMin`. (The subtitle value is supplied by the page model from the match context, not
   hard-coded in XAML.)
2. **Intro copy** — a `TextCaption` (`BrandSage`) reading
   `Rate teammates 0–10, like a great game, and pick one MVP. You can't rate yourself.`
3. **Teammate list** — a `CollectionView` bound to `Teammates`. Each item is a `BrandCard`
   (`CardPadding`) laid out as the wireframe card:
   - a leading row with an `Avatar` (`Size=AvatarMd`, initials; `SemanticProperties.Description` =
     teammate name), the teammate name (`TextBodyStrong`), and a sub-detail (`TextCaption`, e.g.
     `2 goals`, `1 assist`, `clean sheet`);
   - a trailing row (gap `SpaceLg`) with two `IconToggleButton`-styled actions: a **like** heart (`ToggleLikeCommand`,
     `CommandParameter` = the row) and an **MVP** star (`SelectMvpCommand`, `CommandParameter` =
     the row). Each toggle binds its on/off visual to the row's `Liked` / `IsMvp` state via a
     `VisualState`-driven glyph/colour swap, not code-behind logic;
   - below, a 0–10 `Slider` using the shared `RatingSlider` style (`Minimum=0`, `Maximum=10`) bound two-way to the row's `Rating`, with a
     trailing `TextBodyStrong` readout bound to the same value. The page model snaps the slider value
     to an integer so the persisted score is an integer 0–10 (`INV-8`).
4. **`PrimaryButton`** — `Submit ratings`, bound to `SubmitRatingsCommand`; disabled while `IsBusy`.

The page wraps the list + submit button in a `StateView` so the same surface renders loading, empty,
error, offline, and content without page-local branching. Colors, radii, typography, slider track/
thumb, and touch sizes come from `BrandColors.xaml` / `BrandTokens.xaml` / `BrandStyles.xaml`; the
page adds no raw hex colors or emoji.

## Font Awesome contract (`INV-13`)

Pictograms use the bundled Font Awesome Free families registered for the Welcome Back screen
(`FontAwesomeSolid` / `FontAwesomeBrands`) and reference the typed glyph catalog
(`Resources/Fonts/FontAwesomeGlyphs.cs`) — no inline Unicode literals. Required glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Header back | Solid | `arrow-left` |
| Like toggle (off / on) | Solid | `heart` |
| MVP toggle (off / on) | Solid | `star` |

The like and MVP glyphs change colour/fill emphasis between their off and on states (e.g. `BrandSage`
→ `BrandGreen` for a liked card, `BrandSage` → `Warning` for the MVP card) through a `VisualState`
bound to `Liked` / `IsMvp`; the glyph identity is unchanged. Font Awesome is for pictograms only; body
copy stays on the registered Open Sans / Segoe Semibold brand typography. Every informational or
interactive glyph carries a `SemanticProperties.Description` (`Like player`, `Select match MVP`).

## MVVM, data & rules

`RateTeammatesPage` → `RateTeammatesPageModel` (page code-behind only calls `InitializeComponent`; no
validation, navigation, selection, or data logic in code-behind). The page model depends only on the
seed-backed `IStatsClient` abstraction and a navigation service — never on the API or `HttpClient`.

`RateTeammatesPageModel` exposes:

- `MatchSubtitle` — the header subtitle (`Sat · Marina Field`) from the match context;
- `Teammates` — an observable collection of teammate rows, each row a small bindable item with
  `PlayerId`, `Name`, `Detail`, `Initials`, `Rating` (int, two-way, 0–10), `Liked` (bool, two-way),
  and `IsMvp` (bool, read-only to the row; set only through `SelectMvp`);
- `SelectedMvp` — the single teammate currently marked MVP (or none); setting it clears `IsMvp` on
  every other row so exactly one row is MVP at a time (`STAT-5`);
- `IsBusy` — request/submit-in-flight flag that drives the `StateView` Loading state and disables the
  primary action so submit cannot run twice;
- `State` — drives the `StateView` (Loading / Empty / Error / Offline / Content);
- `BackCommand` (`ICommand`) — returns to the previous screen via Shell navigation;
- `ToggleLikeCommand` (`ICommand`, parameter = row) — flips that row's `Liked`, independent of all
  other rows, at most once per teammate (`STAT-4`);
- `SelectMvpCommand` (`ICommand`, parameter = row) — sets that row as the single `SelectedMvp`, or
  clears the selection when the already-selected row is chosen again;
- `SubmitRatingsCommand` (`ICommand`) — sends each teammate's integer `Rating`, `Liked`, and the
  single MVP selection to `IStatsClient` for the current match, then navigates back on success.

On appearance the page model sets Loading and calls `IStatsClient` for the current match's rateable
teammates **excluding the signed-in rater** (`INV-8`: the rater is never in `Teammates`, so the UI
offers no way to rate, like, or MVP yourself). It maps results to `Content` (or `Empty` when none),
`Error` on a recoverable failure, or `Offline` when connectivity is unavailable. The `Rating` setter
coerces to an integer in `[0,10]`; `SelectMvp` keeps MVP single-select; `ToggleLike` keeps like
per-row. `SubmitRatingsCommand` builds the per-teammate payload from `Teammates` and `SelectedMvp`
only — the rater's id never appears in the submission.

`IStatsClient` (defined in SEED-1, [`../../design.md`](../../design.md) §12) gains the rate-teammates
seam this screen consumes: load the match's rateable teammates for the current rater, and submit the
match ratings (per-teammate score + like and the single MVP). The seed implementation returns the
deterministic teammate fixtures from the `rate` wireframe (Kola T. · `2 goals`, Jide D. · `1 assist`,
Sade M. · `clean sheet`), never includes the rater, and accepts the submission without I/O. The typed
API client implements the same methods unchanged at M11.1.

## States

initial / loading · content (one or more rateable teammates) · empty (no rateable teammates) ·
recoverable error (retry) · offline (retry on reconnect) · submitting (primary action busy,
single-flight). Each is rendered by `StateView`; the page model never shows a half-populated list
behind a spinner and never lets a second submit start while one is in flight.

## Test design (`Client.Tests`) — STAT-8 slice

- appearance loads the match's rateable teammates through a mocked `IStatsClient` and exposes one row
  per teammate in the wireframe shape (name, detail, initials);
- the signed-in rater is never present in `Teammates` (no self-vote, `INV-8`);
- each row's `Rating` is an integer constrained to `[0,10]` (values below 0 / above 10 / non-integer
  are coerced into bounds);
- `ToggleLikeCommand` flips only the target row's `Liked` and never affects another row (one like per
  peer, `STAT-4`);
- `SelectMvpCommand` keeps MVP single-select: selecting a second teammate clears the first, exactly
  one row is MVP, and re-selecting the marked teammate clears the MVP (`STAT-5`);
- `SubmitRatingsCommand` sends every teammate's rating, like state, and the single MVP to the client
  for the current match, excludes the rater, and runs once while `IsBusy`;
- an empty result drives `StateView` Empty; a failure drives Error; offline drives Offline; retry
  re-requests the teammate list;
- `BackCommand` requests the correct navigation;
- icon controls expose semantic descriptions; the list stays scrollable and uncut at large text and
  the narrowest supported width.
