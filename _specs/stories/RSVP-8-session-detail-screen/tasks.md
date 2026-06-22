# RSVP-8 — Session detail screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the RSVP-8 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [~] **M11.8a** Implement `SessionDetailPage` and `SessionDetailPageModel` directly from the
  `session` wireframe: `BrandHeader` with back button and venue title, When/Where `BrandCard`,
  capacity `BrandCard` with `CapacityBar` and the warning `Badge`, the "Going · 16" and "Waitlist · 3"
  `SectionHeader` + `PlayerRow` lists (including the "+ 12 more going" affordance), and the
  "RSVP — I'm going" `PrimaryButton` with the "Confirmed via Pickup Pal" note. Use only shared brand
  resources and Font Awesome glyphs; no emoji, no page-local hex; code-behind is `InitializeComponent`
  only.
  — Stories: `RSVP-8`, `INV-13` · Projects: MAUI client · Depends on: M11.0, M11.0a, M11.0b.

- [~] **M11.8b** Bind `SessionDetailPageModel` to the seed `ISessionsClient` / `IRosterClient`: load
  `Session`, `GoingRoster`, and ordered `WaitlistRoster` (positions 1..n, guest flag, "next up");
  surface the current player's "· you" marker and the "+ 12 more going" affordance. No backend.
  — Stories: `RSVP-8`, `SEED-1` · Projects: MAUI client · Depends on: M11.8a, M11.0b.

- [x] **M11.8c** Implement the RSVP toggle: `ToggleRsvpCommand` optimistically flips `IsGoing`,
  records/withdraws going intent through the seed roster client, reverts on failure, and toggles the
  `PrimaryButton` between "RSVP — I'm going" and the confirmed state with the "Confirmed via Pickup
  Pal" note. Records attendance intent only — never check-in/attendance outcome (`INV-12`).
  — Stories: `RSVP-8`, `INV-12` · Projects: MAUI client · Depends on: M11.8b.

- [x] **M11.8d** Wire the screen states through `StateView` (Loading / Empty / Error / Offline /
  Content) and the `BrandHeader` `BackCommand` / `StateView` retry to `BackCommand` / `RefreshCommand`.
  — Stories: `RSVP-8` · Projects: MAUI client · Depends on: M11.8b.

- [~] **M11.8e** (RSVP-8 slice) `Client.Tests`: the page model exposes the wireframe copy; going and
  waitlist load from the seed `IRosterClient` with ordered positions and the guest flag; the RSVP
  toggle flips `IsGoing`, calls the seed client, reverts on failure, and records intent only
  (`INV-12`); `StateView` resolves Loading → Content and surfaces Error/Offline with retry; icon
  controls expose semantic descriptions; the page stays scrollable and uncut at large text and the
  narrowest width. Build `net10.0-windows10.0.19041.0`.
  — Stories: `RSVP-8`, `INV-12`, `INV-13` · Depends on: M11.8a–M11.8d.

  Current: seed detail/roster loading, ordered waitlist and guest state, all `StateView` outcomes,
  and optimistic RSVP intent with failure rollback have passing tests. Remaining: match the
  wireframe's collapsed going roster and `+ 12 more going` affordance, then add icon-semantic and
  large-text/narrow-width verification plus the required light/dark device pass.

**Prerequisites:** M11.0 (reusable UI foundation), M11.0a (Font Awesome glyph catalog), M11.0b (seed
client interfaces + `IRosterClient` / `ISessionsClient` fixtures — see
[`SEED-1`](../SEED-1-seed-data-providers/requirements.md)).

**Done when:** the screen reproduces the `session` wireframe exactly from shared resources, all
RSVP-8 scenarios have passing `Client.Tests` against the seed clients, the RSVP toggle records intent
only, no emoji/raw hex are used, and the client builds.
