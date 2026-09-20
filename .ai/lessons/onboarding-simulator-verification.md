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

## Update 2026-09-16 (GRP-1)

- Authenticated screens are reachable the same way: `signed-in` completes the seed sign-in and lands
  on Sessions; `groups-choose` then routes to `//link-group`; `profile` routes to `//profile`. From
  Profile the membership screens (My groups, Members, Groups & admins) are one tap away, and the
  iOS Simulator MCP `tap`/`swipe` actions work headlessly for that (the `inspect` action may be
  unavailable, so read coordinates off a `simctl` screenshot: points = pixels / 3 on iPhone 17 Pro).
- The seed login stores tokens ("remember this device"), so every later launch restores the Shell
  on its own. The hook must check `Windows[0].Page is Shell` before pushing onboarding pages, or
  `OnboardingNavigator` throws "Onboarding pages require the Welcome Back navigation stack" into a
  modal error over the restored Sessions tab.

## Rule

For any new pre-auth screen, add it to the Debug screen hook so it can be reviewed headlessly, and
prefer `ProgressBar` over `CapacityBar` for time-based bars (`CapacityBar` colours "full" as danger
and announces "places filled").
