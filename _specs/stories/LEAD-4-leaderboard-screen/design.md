# LEAD-4 — Leaderboard screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, seed strategy) lives in [`../../design.md`](../../design.md); the reusable UI
contract is [`../../client-ui.md`](../../client-ui.md); the visual source of truth is
[`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html)
(`leaderboard` screen). This screen's Font Awesome contract implements `INV-13`.

The leaderboard is the **Stats** destination inside the authenticated Shell tab bar. It presents
season read projections (`LEAD-1`/`LEAD-2`) and never maintains totals client-side. In the UI-first
phase it binds to a seed `ILeaderboardClient` (see `../../design.md` §12); swapping in the typed API
client requires no page or page-model change.

## Screen composition

`LeaderboardPage` uses a vertically scrollable layout inside the authenticated tab Shell (matches the
wireframe order/hierarchy):

1. Header row: `Leaderboard` (`TextH1`) on the left and a season `Badge` (`Success` variant, e.g.
   `Season 2026`) on the right. The badge carries a chevron glyph indicating the season is selectable;
   season switching is out of scope here and defaults to the current season.
2. `SegmentedControl` with four segments — `Goals`, `Assists`, `Rating`, `MVP` — bound to
   `SelectedMetric`. Pill segments on `BrandMist`; the selected segment uses the `Surface`/`BrandGreen`
   treatment per `client-ui.md` §6.
3. Ranked list: one `PlayerRow` per ranked player, bound to `Rankings`:
   - `LeadingContent` rank indicator (`TextBodyStrong`) — a numeric rank, except rank 1 which shows the Font
     Awesome `trophy` glyph;
   - `Avatar` (initials or photo, `AvatarMd`);
   - name (`TextBodyStrong`) over a `position · apps` sub-detail (`TextCaption`, `BrandSage`);
   - trailing metric value (`TextBodyStrong`); rank 1's value uses the gold/`Warning`-amber accent.
   - The rank 1 row is tinted as the leader (gold treatment) to distinguish first place by more than
     color alone (trophy glyph + emphasis), satisfying the accessibility rule in `client-ui.md` §8.
4. Inclusion/tie-break footnote (`TextCaption`, `BrandSage`) below the list, bound to
   `Rankings.Note`, replaced whenever the metric changes.
5. `StateView` wraps the list region for `Loading` / `Empty` / `Error` / `Offline`; the ranked list
   is the `Content` body.
6. Bottom navigation is the Shell `TabBar` (Sessions / **Stats** / Profile) themed by `BrandStyles`,
   with Stats active. It is not a page-local control.

Colors, radii, typography, avatars, badges, segments, and touch sizes come from
`BrandColors.xaml`/`BrandTokens.xaml`/`BrandStyles.xaml` and the shared `Controls/`; the page adds no
raw hex colors or emoji.

## Font Awesome contract (`INV-13`)

Pictograms come from the bundled Font Awesome Free fonts registered for `INV-13`
(`FontAwesomeSolid`/`FontAwesomeBrands`) via the typed glyph catalog — no inline Unicode literals.
Required glyphs:

| Purpose | Family | Icon |
|---|---|---|
| Leader (rank 1) indicator | Solid | `trophy` |
| Season badge affordance | Solid | `chevron-down` |
| Stats tab | Solid | `trophy` |
| Sessions tab | Solid | `calendar-day` |
| Profile tab | Solid | `user` |

Each informational/interactive glyph carries a `SemanticProperties.Description` (e.g. the rank 1
trophy reads "Leader"). Font Awesome is for pictograms only; names, values, and the footnote stay on
the Open Sans / Segoe Semibold brand typography.

## MVVM & navigation

`LeaderboardPage` → `LeaderboardPageModel` (page code-behind only calls `InitializeComponent`; no
validation, querying, or navigation logic in XAML code-behind).

`LeaderboardPageModel` owns:

- `Season` — the current season label shown in the badge (defaults to the current season);
- `SelectedMetric` — the chosen axis (`Goals|Assists|Rating|MVP`); changing it re-queries;
- `Rankings` — the ordered rows for the selected metric plus the metric's inclusion/tie-break note,
  exposed for binding to the list and footnote;
- `IsBusy` — drives the busy/loading state and guards re-entrant queries;
- `SelectMetricCommand` — sets `SelectedMetric` and loads that metric's ranking;
- `OpenPlayerCommand` — navigates to the tapped player's Profile (`LEAD-2`);
- `RefreshCommand` — re-requests the current metric's ranking (also the `StateView` retry).

```text
LeaderboardPage
  -> LeaderboardPageModel
       -> ILeaderboardClient.GetRankingAsync(seasonId, metric)   // seed-backed this phase
       -> IAppNavigator.GoToPlayerProfileAsync(playerId)
```

`ILeaderboardClient` is the seam from `../../design.md` §12: a `SeedLeaderboardClient` in
`SouthBaySoccer/SeedData/` returns the wireframe fixtures — the Goals/Assists/Rating/MVP rankings,
each with its `position · apps` sub-detail, metric value, and inclusion/tie-break note — ordered
descending with `LEAD-3` tie-breaks already applied. The page model treats every ranking as an opaque
read projection; it does not recompute or re-sort totals (`INV-6`/`INV-7`).

## States

- **Loading** — a metric query is in flight (`IsBusy`); shown on first load and on each metric switch.
- **Content** — the ranked list and footnote for the selected metric.
- **Empty** — the selected metric has no ranked players.
- **Error** — a recoverable service failure; offers retry (`RefreshCommand`).
- **Offline** — the device is offline; offers retry.

Selecting a metric while a query is in flight does not issue a second concurrent request; the latest
selection wins and the list reflects the metric that resolved last.

## Test design (`Client.Tests`) — LEAD-4 slice

- the page model exposes the four metrics, the season label, and the wireframe header/footnote copy;
- selecting each metric calls `ILeaderboardClient` for that axis and swaps `Rankings` to the matching
  ranked list (segment switch swaps the ranking);
- rankings are presented in the seed order with `LEAD-3` tie-breaks intact (fewer appearances, then
  assists for goals) — the page model does not re-sort;
- rank 1 is flagged as the leader (trophy indicator + gold value) and remaining ranks are sequential;
- `OpenPlayerCommand` navigates to the tapped player's Profile exactly once;
- loading/empty/error/offline map to the correct `StateView` state and retry re-requests the current
  metric;
- icon controls expose semantic descriptions and the list stays scrollable and uncut at large text
  and the narrowest supported width.
