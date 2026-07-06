# AUTH-7 — Welcome Back screen · Tasks

Implementation tasks for [`requirements.md`](requirements.md) / [`design.md`](design.md). These are
the AUTH-7 slice of milestone **M11**; the full milestone roadmap and dependency graph live in
[`../../tasks.md`](../../tasks.md). Status: `[ ]` todo · `[~]` in progress · `[x]` done.

- [x] **M11.0a** Add licensed Font Awesome Free Solid and Brands font resources, register
  `FontAwesomeSolid`/`FontAwesomeBrands`, and add a typed glyph catalog. Replace emoji/text
  pictograms used by the Welcome Back screen with Font Awesome glyphs + semantic descriptions.
  — Stories: `INV-13`, `AUTH-7` · Projects: MAUI client · Depends on: M11.0.

- [x] **M11.3a** Implement `WelcomeBackPage` and `WelcomeBackPageModel` directly from the first
  `signin` wireframe: branded header, welcome copy, phone input, phone sign-in action, security notice,
  Pickup Pal bot card, divider, signup action, and caption. Use only shared brand resources and Font
  Awesome glyphs; no emoji or page-local hex.
  — Stories: `AUTH-7`, `INV-13` · Projects: MAUI client · Depends on: M11.0a.

- [~] **M11.3d** (AUTH-7 slice) `Client.Tests`: signed-out startup routes to `WelcomeBackPage`; a
  valid restored session bypasses it; wireframe copy is exposed by the page model; icon controls
  expose semantic descriptions; the page stays scrollable and uncut at large text and the narrowest
  width. Build `net10.0-windows10.0.19041.0`.
  — Stories: `AUTH-7`, `INV-13` · Depends on: M11.3c.

  Current: startup restore, page-model copy, icon semantics, Windows/Android builds, and focused
  client tests are implemented. Automated large-text/narrow-viewport visual verification remains.

**Prerequisites:** M11.0 (reusable UI foundation, done). **Related task slices:**
[`AUTH-8`](../AUTH-8-continue-with-whatsapp/tasks.md) (phone sign-in flow), [`AUTH-9`](../AUTH-9-pickup-pal-actions/tasks.md) (external actions).

**Done when:** the screen reproduces the first wireframe exactly from shared resources, all AUTH-7
scenarios have passing `Client.Tests`, no emoji/raw hex are used, and the client builds.

