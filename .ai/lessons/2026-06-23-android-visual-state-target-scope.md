---
name: android-visual-state-target-scope
description: Cross-element VisualState TargetName setters can pass builds but crash when Android Shell materializes a tab
area: maui
created: 2026-06-23
---

**Context:** A MAUI Shell root page used `AdaptiveTrigger` setters to change named sibling grids and
their attached row/column properties.

**Problem:** Windows and Android builds passed, but opening the Profile tab on Android threw
`XamlParseException: Cannot resolve 'IdentityRow' as Setter Target`. Static XAML tests only confirmed
that the setters existed, so they encoded the broken pattern instead of validating runtime safety.

**Rule:** Do not use cross-element `TargetName` setters for responsive Shell-root layouts without an
Android runtime verification. Prefer stable Grid/Flex layouts that naturally measure and wrap. Add a
regression test that rejects the failing `AdaptiveTrigger`/`TargetName` pattern when it caused a
platform crash.
