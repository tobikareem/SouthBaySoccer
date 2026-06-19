---
name: do-not-use-ui-showcase-as-shell-home
description: Keep the reusable-control showcase off the authenticated Shell startup path
type: lesson
created: 2026-06-18
---

The Windows app entered a high-CPU WinUI hang after successful Seed authentication because
`AppShell` configured the expanded `DesignSystemPage` showcase as its initial `home` route. Unit
tests mocked navigation and therefore did not instantiate the real Shell/page tree.

**Rule:** A design-system/UI-library showcase is a secondary diagnostic route, never the production
Shell landing page. Keep `home` on a stable product/dashboard page and give the showcase its own
route. Add a XAML structure test that asserts the route mapping, and perform a real Windows startup
smoke test whenever authentication replaces the root page.
