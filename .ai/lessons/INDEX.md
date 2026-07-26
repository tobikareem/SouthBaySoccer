# Lessons — Index

One line per lesson. Skim this at the start of a task; read the full entry when relevant.

- [maui-build-target-framework](maui-build-target-framework.md) — Build a specific TFM, not the bare csproj, for this single-project MAUI app
- [avoid-placeholder-gitkeep-trees](2026-06-18-avoid-placeholder-gitkeep-trees.md) — Create source directories with real files instead of committing placeholder trees
- [do-not-use-ui-showcase-as-shell-home](2026-06-18-do-not-use-ui-showcase-as-shell-home.md) — Keep the UI Library showcase off the authenticated Shell startup path
- [android-visual-state-target-scope](2026-06-23-android-visual-state-target-scope.md) — Cross-element VisualState TargetName setters can build successfully but crash when Android Shell creates a tab
- [maui-xaml-commandparameter-types](2026-06-25-maui-xaml-commandparameter-types.md) � XAML CommandParameter literals are strings and can crash typed CommunityToolkit commands at runtime

- [android-defer-session-restore](2026-07-06-android-defer-session-restore.md) - Defer stored-token restore until after the startup page loads to avoid Android ANRs
- [ef-retry-strategy-manual-transactions](2026-07-21-ef-retry-strategy-manual-transactions.md) - EnableRetryOnFailure rejects bare BeginTransactionAsync; wrap transactions in a retry-idempotent execution-strategy delegate
- [maui-ios-ci-publish-pitfalls](2026-07-25-maui-ios-ci-publish-pitfalls.md) - CI iOS publish needs macos-26 + maui-ios workload + a csproj-scoped TFM conditional; never override TargetFrameworks globally
