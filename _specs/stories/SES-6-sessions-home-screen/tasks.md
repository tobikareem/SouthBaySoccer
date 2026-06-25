# SES-6 — Sessions (home) screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the SES-6 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [x] **M11.SES6.a** Implement `SessionsHomePage` and `SessionsHomePageModel` directly from the `home`
  wireframe: `Saturday crew` / `Good morning, Tobi` greeting with `Paid` Badge and bell, green
  next-match hero BrandCard, separate `Submit your latest stats` BrandCard, `Coming up`
  SectionHeader with `See schedule`, remaining-session CollectionView, and the Shell Sessions/Stats/
  Profile TabBar. Use only shared brand resources and Font Awesome glyphs with semantic descriptions;
  no emoji or page-local hex. Code-behind calls only `InitializeComponent`.
  — Stories: `SES-6`, `INV-13` · Projects: MAUI client · Depends on: M11.0c, M11.0b.

- [x] **M11.SES6.b** Bind `SessionsHomePageModel` to the SEED-1 `ISessionsClient`: load one dashboard
  projection and expose `FeaturedSession`, `StatsPrompt`, `ComingUpSessions`, `DuesStatus`, and
  `IsBusy`; wire
  the `OpenSession`, `OpenMatchStats`, `JoinWaitlist`, and `Refresh` commands. Page model depends on
  the client interface and a navigation service only — no API or `HttpClient` reference.
  — Stories: `SES-6` · Projects: MAUI client · Depends on: M11.SES6.a, M11.0b.

- [x] **M11.SES6.c** Drive loading / empty / error / offline / content through `StateView`; map
  `ISessionsClient` outcomes to the correct state; `RefreshCommand` re-runs the load from any
  non-content state.
  — Stories: `SES-6` · Projects: MAUI client · Depends on: M11.SES6.b.

- [x] **M11.SES6.d** Add `Client.Tests` for the SES-6 slice: appearance loads the featured match,
  stats prompt, dues status, and coming-up sessions
  through a mocked `ISessionsClient`; empty/error/offline drive the matching `StateView` state;
  `RefreshCommand` reloads; `OpenSession` / `OpenMatchStats` / `JoinWaitlist` request the correct
  targets; icon controls expose semantic descriptions; the list stays scrollable and uncut at large
  text and the narrowest width. Build `net10.0-windows10.0.19041.0`.
  — Stories: `SES-6`, `INV-13` · Depends on: M11.SES6.c.

  Current: dashboard loading, all five `StateView` outcomes, refresh, session/stats navigation, and
  waitlist commands have passing page-model tests. The semantic/responsive contract is now covered by
  automated XAML-contract tests in `SessionScreensXamlTests` (notifications semantic description,
  `ScrollView` so content stays uncut, typed Font Awesome glyphs / no emoji, theme-token colours / no
  raw hex). Suite green (141/141, Debug + Release); Windows (`net10.0-windows10.0.19041.0`) and
  Android (`net10.0-android`) builds succeed with zero new warnings.

  Sprint 01 closeout: interactive on-device light/dark visual sign-off is accepted for Windows + Android; no SES-6 work remains in review.

**Prerequisites:** M11.0c (shared first-wave UI extensions), M11.0b (seed-data providers —
`ISessionsClient`). **Related task slice:** M11.3e in
[`../../tasks.md`](../../tasks.md) (remaining profile/sessions/RSVP screens).

**Done when:** the screen reproduces the `home` wireframe from shared resources, binds to the seed
`ISessionsClient`, renders all five StateView states, all SES-6 scenarios have passing
`Client.Tests`, no emoji/raw hex are used, and the client builds.
