# SES-6 — Sessions (home) screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, persistence, seed-data strategy) lives in [`../../design.md`](../../design.md); the
reusable UI contract is [`../../client-ui.md`](../../client-ui.md); the visual source of truth is
the `home` screen in
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
This screen's Font Awesome contract implements `INV-13`.

The screen is the first authenticated route inside the signed-in Shell, hosting the Sessions tab. In
this UI-first phase it binds to a **seed** `ISessionsClient`, swapped for the typed API client at
M11.1 with no page or page-model change ([`../../design.md`](../../design.md) §12).

## Screen composition

`SessionsHomePage` is a `BrandPage` with a vertically scrollable list of cards above a Shell tab bar,
composed entirely from the reusable control catalog ([`../../client-ui.md`](../../client-ui.md) §6)
and brand tokens — no page-local hex, font sizes, or emoji.

1. **Greeting header** — a shared-token greeting row: a muted group line ("Saturday crew")
   over a `TextH1` greeting ("Good morning, Tobi"), and trailing content holding a `Paid` `Badge`
   (`Variant=Success`, dues glyph) and a notifications bell icon button (Font Awesome `bell`,
   semantic description "Notifications").
2. **Next-match hero** — a tappable `BrandCard` with `IsHero=true`: `Next match · in 2 days`,
   `Marina Field · Saturday pickup`, date/time/format `MetadataChip`s, `You're going`, and
   `View details`.
   Bound to `OpenSessionCommand` for the featured session.
3. **Stats prompt** — a compact tappable `BrandCard` with an `IconButton`-styled chart glyph, title
   `Submit your latest stats`, caption `2 goals entered · captain confirmation pending`, and a
   chevron. Bound to `OpenMatchStatsCommand`.
4. **`SectionHeader`** — text `Coming up`, with `See schedule`.
5. **Coming-up session list** — a `CollectionView` bound to `ComingUpSessions`. Each card is a
   tappable `BrandCard` (`OpenSessionCommand`, `CommandParameter` = the session item) showing:
   - title (`TextH2`, e.g. `Marina Field · 7v7`);
   - a status `Badge` (`Going` → `Variant=Success`; `Full` → `Variant=Danger`);
   - a date row of calendar/clock glyph chips (`TextCaption`);
   - a `CapacityBar` (`Current`/`Max`, label `16 / 20 going`);
   - a `View` affordance for going/open sessions, or, when the session is full, a `Join waitlist`
     action bound to `JoinWaitlistCommand` with the remaining waitlist count.
6. **Shell `TabBar`** — Sessions (active) / Stats / Profile, themed by `BrandStyles` (not a custom
   control). Tab pictograms use Font Awesome glyphs with semantic names.

The page wraps its content list in a `StateView` so the same surface renders loading, empty, error,
offline, and content without page-local branching.

Colors, radii, typography, and touch sizes come from `BrandColors.xaml` / `BrandTokens.xaml` /
`BrandStyles.xaml`; the page adds no raw hex colors or emoji.

## Font Awesome contract (`INV-13`)

Pictograms use the bundled Font Awesome Free families registered for the Welcome Back screen
(`FontAwesomeSolid` / `FontAwesomeBrands`) and reference the typed glyph catalog
(`Resources/Fonts/FontAwesomeGlyphs.cs`) — no inline Unicode literals. Required glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Dues-paid status | Solid | `circle-check` |
| Notifications | Solid | `bell` |
| Stats banner / submit | Solid | `chart-simple` |
| Session date | Solid | `calendar` |
| Session time | Solid | `clock` |
| Capacity / squad size | Solid | `users` |
| Open / View affordance | Solid | `arrow-right` / `chevron-right` |
| Tabs: Sessions / Stats / Profile | Solid | `calendar` / `trophy` / `user` |

Font Awesome is for pictograms only; body copy stays on the registered Open Sans / Segoe Semibold
brand typography. Every informational or interactive glyph carries a `SemanticProperties.Description`.

## MVVM, data & navigation

`SessionsHomePage` → `SessionsHomePageModel` (page code-behind only calls `InitializeComponent`; no
validation, navigation, or data logic in code-behind). The page model depends only on the
seed-backed `ISessionsClient` abstraction and a navigation service — never on the API or `HttpClient`.

`SessionsHomePageModel` exposes:

- `FeaturedSession` — the next-match hero projection.
- `StatsPrompt` — the latest-match stats prompt projection.
- `ComingUpSessions` — remaining session cards (title, badge variant/text, date/time,
  current/max capacity, waitlist count, `IsFull`);
- `DuesStatus` — the player's dues badge state;
- `IsBusy` — request-in-flight flag driving the `StateView` Loading state;
- `State` — drives the `StateView` (Loading / Empty / Error / Offline / Content);
- `OpenSessionCommand` (`ICommand`, parameter = session) — navigates to Session detail;
- `OpenMatchStatsCommand` — navigates to Match stats;
- `JoinWaitlistCommand` (`ICommand`, parameter = session) — joins the waitlist for a full session;
- `RefreshCommand` — reloads dues status and upcoming sessions from `ISessionsClient`.

On appearance the page model sets Loading, calls `ISessionsClient` for the dues status and upcoming
sessions, then maps results to `Content` (or `Empty` when none), `Error` on a recoverable failure,
or `Offline` when connectivity is unavailable. Navigation commands route through Shell to the Session
detail and Match stats screens; tab switching is Shell `TabBar` navigation, not a command.

## States

initial / loading · content (one or more upcoming sessions) · empty (no upcoming sessions) ·
recoverable error (retry) · offline (retry on reconnect). Each is rendered by `StateView`; the
page model never shows a half-populated list behind a spinner.

## Test design (`Client.Tests`) — SES-6 slice

- appearance loads the complete dashboard projection through a mocked `ISessionsClient`;
- a successful load exposes the wireframe-shaped session items and dues status to the page;
- an empty result drives `StateView` Empty; a failure drives Error; offline drives Offline;
- `RefreshCommand` re-runs the load from a non-content state;
- `OpenSessionCommand` and `OpenMatchStatsCommand` request the correct navigation targets;
- `JoinWaitlistCommand` is invoked only for full sessions and carries the right session;
- icon controls expose semantic descriptions; the list stays scrollable and uncut at large text and
  the narrowest supported width.
