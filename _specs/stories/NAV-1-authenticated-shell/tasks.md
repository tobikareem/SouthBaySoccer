# NAV-1 — Authenticated Shell & bottom tabs · Tasks

NAV-1 slice of milestone **M11** (full roadmap: [`../../tasks.md`](../../tasks.md)). Sprint 01.
Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [x] **M11.NAV1.a** Implement the authenticated `AppShell` with a `TabBar` (Sessions / Stats /
  Profile) and Shell routes `sessions`, `stats`, `profile`; theme the tab bar/header from
  `BrandStyles`/`BrandColors` (active = `BrandGreen`, inactive = `BrandSage`); use Font Awesome tab
  glyphs with semantic names (no emoji/hex). Declare root routes in `AppShell`; register their page
  types in `MauiProgram.cs`. Implement
  `IAuthenticationNavigator`'s "go to authenticated Shell" target as a **root replacement** so the
  sign-in route is off the authenticated back stack. Provide a `StateView` placeholder for the
  not-yet-built Stats tab.
  — Stories: `NAV-1`, `INV-13` · Projects: MAUI client · Depends on: M11.0, M11.0a, M11.3c (AUTH-8 navigator).

- [x] **M11.NAV1.b** Wire startup routing: the startup coordinator restores a session and roots the
  Shell directly, otherwise roots Welcome Back; sign-in success routes into the Shell (Sessions active).
  — Stories: `NAV-1`, `AUTH-8` · Projects: MAUI client · Depends on: M11.NAV1.a.

- [x] **M11.NAV1.c** `Client.Tests`: verified sign-in → Shell (Sessions active); restorable session at
  startup → Shell (no Welcome Back); no session → Welcome Back; tab switch activates the tab and keeps
  the bar; back from a root tab does not reach Welcome Back; unbuilt tab shows the placeholder; tab
  icons are Font Awesome + semantic names with ≥44 dip targets. Build `net10.0-windows10.0.19041.0`.
  — Stories: `NAV-1` · Depends on: M11.NAV1.b.

  Current: implementation, automated coverage, and manual Sprint 01 closeout sign-off are complete. The authenticated root uses one native `TabBar`; Stats and Profile render `StateView` placeholders; tab icons use typed Font Awesome resources with semantic names; and the navigator replaces the window root. Client tests pass, Windows/Android builds succeed, and tab switching, root-tab back behavior, screen-reader output, and light/dark modes are accepted for Sprint 01.

**Prerequisites:** M11.0 (UI foundation, done), M11.0a (Font Awesome, done), AUTH-8 challenge flow
(`M11.3c`). **Tab destinations:** `SES-6` (Sessions) and `PROF-5` (Profile) this sprint; `LEAD-4`
(Stats) in Sprint 02 — placeholder until then.

**Done when:** sign-in and startup both land on the tabbed authenticated Shell, tabs switch and the
sign-in route is unreachable from inside the Shell, the Shell is brand-themed with accessible Font
Awesome tab icons, the Stats placeholder renders, all NAV-1 `Client.Tests` pass, and the client builds.
