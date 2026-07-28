---
name: maui-staticresource-keys-crash-at-launch
description: Undefined StaticResource keys build clean and crash at page inflation; on the startup page that is a TestFlight launch crash
area: maui
created: 2026-07-28
---

**Context:** The M14 announcement work added a notification badge to `SessionsHomePage.xaml` and new
`AnnouncementCard` / `GroupChoiceRow` / `AdminBroadcastPage` markup. It built clean on every target
and passed the full test suite, but the TestFlight build crashed on launch on an iPhone 17 Pro Max.

**Problem:** Six `{StaticResource}` keys were referenced but never defined anywhere in the app —
`BrandWhite`, `BrandPineDark`, `StatusDangerLight`, `BorderLight`, `BorderDark`. Keys in
app-level merged dictionaries resolve at **runtime**, not at compile time, so XamlC and
`MauiXamlInflator=SourceGen` accept them silently. `SessionsHomePage` is the Shell's first
`ShellContent`, so Shell inflated it during initial view-controller load and the lookup threw.

The Xcode crash report showed only:

```
Last Exception Backtrace
  0 CoreFoundation  __exceptionPreprocess
  1 libobjc.A.dylib objc_exception_throw
  2 SouthBaySoccer  0x1021d8000            <- unsymbolicated
  4 UIKitCore       -[UIViewController _sendViewDidLoadWithAppearanceProxyObjectTaggingEnabled]
```

The `_sendViewDidLoad…` frame is the tell: the crash is *page inflation*, not page-model logic.
Managed exceptions surface through `objc_exception_throw`, and without a dSYM the managed frames are
bare addresses — so the stack names the phase, never the key.

**Rule:** Never introduce a `StaticResource` key without defining it in a merged dictionary in the
same change; prefer an existing token over inventing a name (`BorderLight` should have been
`BrandLineLight`). `StaticResourceKeyTests` in the client test project now cross-checks every
`{StaticResource}` reference in every app XAML file against every `x:Key` definition, and the csproj
mirrors `SouthBaySoccer\**\*.xaml` wholesale so new pages are covered automatically — never
hand-list the files, since a stale list is what let this through.

When a MAUI iOS crash report points at `_sendViewDidLoadWithAppearanceProxyObjectTaggingEnabled`,
suspect XAML inflation on the startup page first: undefined resource keys, missing `BindableProperty`
backing a control attribute, or a cross-element `VisualState` `TargetName`
(see [android-visual-state-target-scope](2026-06-23-android-visual-state-target-scope.md)).
