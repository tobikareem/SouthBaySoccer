# NAV-1 — Authenticated Shell & bottom tabs · Design

Realizes [`requirements.md`](requirements.md). The reusable brand/Shell theming is in
[`../../client-ui.md`](../../client-ui.md) (delivered by `M11.0`); the auth/startup routing seam is in
[`AUTH-8 design`](../AUTH-8-continue-with-whatsapp/design.md). This story wires the authenticated Shell
structure, tabs, and route composition — it adds no data dependency and no business logic.

## Shell structure

The app has two top-level navigation regions:

- **Authentication region** — the `WelcomeBackPage` route, shown when no valid session exists. Outside
  the tabbed Shell (no flyout, no tabs).
- **Authenticated Shell** — a `TabBar` with three `Tab`s, each a Shell route:

| Tab | Route | Destination | Font Awesome glyph (Solid) |
|-----|-------|-------------|----------------------------|
| Sessions | `sessions` | `SES-6` Sessions page (initial tab) | `calendar-days` |
| Stats | `stats` | `LEAD-4` Leaderboard (Sprint 02) — placeholder `StateView` until then | `ranking-star` / `trophy` |
| Profile | `profile` | `PROF-5` Profile page | `user` |

Glyph names are indicative; the typed Font Awesome glyph catalog (`M11.0a`) supplies the constants.
The `TabBar`, tab items, and header are themed from `BrandStyles`/`BrandColors` (no page-local hex);
the active tab uses `BrandGreen`, inactive uses `BrandSage`, matching the wireframe tab bar.

## Routing & startup

```
App start
  └─ startup coordinator (AUTH-8): try restore session from secure storage
       ├─ success → set authenticated Shell as root, Sessions tab active
       └─ no/expired session → set Welcome Back as root (auth region)

Sign-in success (AUTH-8 phone sign-in)
  └─ IAuthenticationNavigator.GoToAuthenticatedShellAsync()
       → replaces the auth region root with the authenticated Shell (Sessions active)

Sign-out (later)
  └─ IAuthenticationNavigator.GoToSignInAsync() → replaces Shell with Welcome Back, clears tokens
```

`IAuthenticationNavigator` already exists in the AUTH-8 flow; NAV-1 implements the
"go to authenticated Shell" target (the Shell + tabs). Root routes are declared in `AppShell`; their page types are registered
with dependency injection in `MauiProgram.cs`.
Root replacement (not push) ensures the sign-in route is **not** on the authenticated back stack
(requirements: sign-in unreachable from inside the Shell).

## Components

- `AppShell` (authenticated) with the three `Tab`s/routes above; brand-themed via shared resources.
- Root route declarations in `AppShell` for `sessions`, `stats`, and `profile`; page registrations
  remain in `MauiProgram.cs`, while Welcome Back remains outside the authenticated Shell.
- Tab destinations are owned by their stories; NAV-1 provides a `StateView` "coming soon" placeholder
  for any tab whose page is not yet implemented (Stats this sprint).
- No page model business logic; navigation/state lives in the navigator + startup coordinator.

## States

Shell itself is structural. The only state surface NAV-1 owns is the **placeholder** `StateView`
(empty/coming-soon) for an unbuilt tab. Each destination owns its own loading/empty/error/offline.

## Test design (`Client.Tests`) — NAV-1 slice

- verified sign-in routes to the authenticated Shell with Sessions active;
- a restorable session at startup shows the Shell directly (no Welcome Back);
- no restorable session shows Welcome Back;
- selecting Stats/Profile activates that tab and keeps the tab bar;
- after entering the Shell, back navigation does not return to Welcome Back;
- an unbuilt tab shows the placeholder `StateView`, not a blank page;
- tab bar/icons use brand resources + Font Awesome glyphs with semantic names; targets ≥ 44 dip.


