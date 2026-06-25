---
name: maui-xaml-commandparameter-types
description: XAML CommandParameter literals are strings and can crash typed CommunityToolkit commands at runtime
area: maui
created: 2026-06-25
---

**Context:** The Captain Assignment screen bound buttons like `CommandParameter="2"` to a
CommunityToolkit `[RelayCommand]` method accepting `int`.

**Problem:** Android materialized the page and called `CanExecute` while applying bindings. The
literal XAML parameter arrived as `System.String`, but `RelayCommand<int>` requires `System.Int32`,
throwing `android.runtime.JavaProxyThrowable` during page construction.

**Rule:** For MAUI XAML command parameters, either provide a typed object explicitly or make the
command accept `object?`/string-safe input and parse at the page-model boundary. Add a regression
test that executes the generated command with the same string value XAML supplies.
