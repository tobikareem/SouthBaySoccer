---
name: onboarding-simulator-verification
description: Verifying pre-auth MAUI screens on the iOS simulator without panel input access; use the Debug env-var screen hook and simctl screenshots
type: lesson
created: 2026-09-14
---

## What happened

While building the AUTH-10 sign-up/sign-in screens, the Claude simulator panel had no input
permission, `xcrun simctl openurl` for the custom scheme raised an "Open in N9ja Bay?" system
dialog that also needed a tap, and `osascript` could not see the Simulator process. Screens
reachable only by tapping could not be exercised.

## What worked

- `App.xaml.cs` (Debug only) reads `N9JABAY_ONBOARDING_SCREEN` on the first Welcome Back `Loaded`
  and opens that screen against Seed data. Launch with
  `SIMCTL_CHILD_N9JABAY_ONBOARDING_SCREEN=<screen> xcrun simctl launch booted com.pickupsoccer.n9jabay`
  and capture with `xcrun simctl io booted screenshot out.png`. Screens: `signup-start`,
  `signup-waiting`, `signup-expired`, `signup-details`, `signup-welcome`, `signin-verify`,
  `signin-waiting`.
- Screens that open WhatsApp bounce to Safari on a simulator (no WhatsApp). Terminate
  `com.apple.mobilesafari` and re-run `simctl launch` for the app bundle to bring it back to the
  foreground before the screenshot.
- Do not use `simctl openurl` with the custom scheme for verification: the OS prompt persists
  across app relaunches and pollutes every later screenshot.

## Rule

For any new pre-auth screen, add it to the Debug screen hook so it can be reviewed headlessly, and
prefer `ProgressBar` over `CapacityBar` for time-based bars (`CapacityBar` colours "full" as danger
and announces "places filled").
