# RSVP-8 — Session detail screen · Design

Realizes [`requirements.md`](requirements.md) on the client architecture. Cross-cutting design
(layers, ports, seed-data strategy) lives in [`../../design.md`](../../design.md) (§12 seed data);
the reusable UI contract is [`../../client-ui.md`](../../client-ui.md); the visual source of truth is
the `session` screen in [`../../../documentation/mobile-wireframes.html`](../../../documentation/mobile-wireframes.html).
This screen reuses the completed control catalog and adds no page-local styles.

In the UI-first phase the going list, waitlist, capacity, and the RSVP action are served by seed
clients ([`SEED-1`](../SEED-1-seed-data-providers/requirements.md)); pages and page models depend
only on the client-service interfaces and are unchanged when the typed API client replaces the seeds
(M11.1). **The RSVP action records attendance intent only** — it never checks the player in or
records an attendance outcome (`INV-12`); check-in is a separate screen.

## Screen composition

`SessionDetailPage` is a `BrandPage` with a `StateView` wrapping a vertically scrollable layout that
matches the wireframe order:

1. `BrandHeader` (`ShowBack=true`, `BackCommand`): green-to-pine header with the back button, a
   "Saturday pickup" eyebrow as `Subtitle`, and the venue title "Marina Field" as `Title`.
2. When/Where `BrandCard`: a date-and-time `PlayerRow`-style row (calendar glyph + `Date and time`
   `TextLabel` + "Sat Jun 20 · 9:00 AM" in `TextBodyStrong`) over a `BrandLine` divider, then a
   location row (map-pin glyph + `Location` label + "Marina Field · 7v7") with a trailing "Map"
   `LinkButton`.
3. Capacity `BrandCard`: a row with "16 / 20 going" (`TextBodyStrong`) and a `Badge`
   (`Variant=Warning`, clock glyph, "closes 1d 4h"), above a `CapacityBar` (`Current=16`, `Max=20`).
4. `SectionHeader` "Going · 16" then a going `PlayerRow` list — each row an `Avatar` (initials),
   `Name`, position as `Detail` (the current player carries a "· you" muted suffix), and a final
   "+ 12 more going" affordance row.
5. `SectionHeader` "Waitlist · 3" then an ordered waitlist `PlayerRow` list — the `Avatar` shows the
   ordinal position number; position 1 ("Tunde B.") carries a `Badge` (`guest`, warning treatment)
   and a "next up" trailing label.
6. `PrimaryButton` "RSVP — I'm going" that toggles to its confirmed state, followed by a subtle
   "Confirmed via Pickup Pal" caption (`TextCaption`) with a plug/connected glyph.

Colors, radii, typography, spacing, and touch sizes come from `BrandColors.xaml` /
`BrandTokens.xaml` / `BrandStyles.xaml`; the page adds no raw hex or emoji. Times arrive UTC and are
formatted to local only at this UI boundary (`NFR-Time`).

## Controls and tokens

| Wireframe element | Control / style |
|-------------------|-----------------|
| Header + back + venue title | `BrandHeader` |
| When/Where and capacity cards | `BrandCard` |
| "16 / 20 going" bar | `CapacityBar` |
| "closes 1d 4h" / "guest" pills | `Badge` (`Warning`) |
| "Going · 16" / "Waitlist · 3" | `SectionHeader` |
| Going / waitlist rows | `PlayerRow` (+ `Avatar`) |
| "RSVP — I'm going" toggle | `PrimaryButton` style |
| Loading / empty / error / offline | `StateView` |

## Font Awesome contract (`INV-13`)

Pictograms are bundled Font Awesome Free glyphs referenced through the typed glyph catalog
(`Resources/Fonts/FontAwesomeGlyphs.cs`) — no inline Unicode literals, no emoji. Each icon carries a
semantic description.

| Purpose | Family | Icon |
|---|---|---|
| Back | Solid | `arrow-left` |
| Date and time | Solid | `calendar` |
| Location | Solid | `location-dot` |
| Deadline / waitlist warning | Solid | `clock` |
| Confirmed-via note | Solid | `plug-circle-check` / `link` |

## MVVM — `SessionDetailPageModel`

`SessionDetailPage` code-behind calls only `InitializeComponent`; all behavior lives in the page
model (`BindableProperty` inputs, `ICommand` outputs — no logic in XAML code-behind).

State exposed by `SessionDetailPageModel`:

- `Session` — When/Where details and capacity (current/max, deadline label) for the header and cards.
- `GoingRoster` — the confirmed going list (avatar, name, position, "you" flag, "+ N more").
- `WaitlistRoster` — the ordered waitlist (position number, name, guest flag, "next up").
- `IsGoing` — whether the current player is confirmed; drives the `PrimaryButton` label/confirmed
  state and the "Confirmed via Pickup Pal" note.
- `IsBusy` — blocks duplicate RSVP taps and reload re-entrancy.
- `State` — the `StateView` state (Loading / Empty / Error / Offline / Content).

Commands:

- `ToggleRsvpCommand` — optimistically flips `IsGoing` and calls the seed roster client to record
  going intent (or withdraw it); on failure it reverts the optimistic flip and surfaces a recoverable
  message. Records **attendance intent only** (`INV-12`).
- `BackCommand` — bound to `BrandHeader.BackCommand`; returns to the previous route.
- `RefreshCommand` — re-loads `Session`, `GoingRoster`, and `WaitlistRoster`; bound to the
  `StateView` retry action.

Dependencies (seed-backed in this phase, swapped by DI in M11.1):

```
SessionDetailPage
  -> SessionDetailPageModel
       -> ISessionsClient   // session details + capacity (seed)
       -> IRosterClient     // going + ordered waitlist; record/withdraw going intent (seed)
       -> navigation service // Back
```

## States

Loading (initial fetch) · Content (roster + capacity populated) · Empty (no going players and no
waitlist) · Error (recoverable roster/session failure, retry) · Offline. The optimistic RSVP toggle
has its own busy state independent of the page `StateView`.

## Test design (`Client.Tests`) — RSVP-8 slice

- the page model exposes the wireframe copy: venue title, "16 / 20 going", "closes 1d 4h",
  "Going · 16", "Waitlist · 3", and the "+ 12 more going" affordance;
- `GoingRoster` and `WaitlistRoster` load from the seed `IRosterClient`; waitlist positions are 1, 2,
  3 in order and position 1 carries the guest flag ("Tunde B.");
- `ToggleRsvpCommand` flips `IsGoing` and calls the seed client; a client failure reverts the
  optimistic state and re-enables the action;
- the RSVP action records intent only and never invokes a check-in/attendance path (`INV-12`);
- the seed clients perform no network/file/database access;
- `StateView` resolves Loading → Content, surfaces Error/Offline, and `RefreshCommand` reloads;
- icon controls expose semantic descriptions; the page stays scrollable and uncut at large text and
  the narrowest supported width.
