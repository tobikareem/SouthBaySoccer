---
name: maui-shell-route-ownership
description: Shell root routes live in AppShell; Routing.RegisterRoute is reserved for detail routes
type: convention
created: 2026-06-22
---

Authenticated root routes such as `sessions`, `stats`, and `profile` are declared in the `AppShell`
visual hierarchy. `MauiProgram.cs` registers their page types with dependency injection; use
`Routing.RegisterRoute` / `AddTransientWithShellRoute` only for detail routes pushed from a root.

**Why:** Registering a Shell-hierarchy root again as a global route risks duplicate-route failures
and obscures which navigation region owns the route.
**How to apply:** Put root `Route` values on `ShellContent` inside the authenticated `TabBar`, keep
Sessions first for startup, and register only non-root detail destinations through Shell route DI.

Related: [[client-reusable-ui]], [[spec-driven-development]]
