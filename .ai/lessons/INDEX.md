# Lessons — Index

One line per lesson. Skim this at the start of a task; read the full entry when relevant.

- [maui-build-target-framework](maui-build-target-framework.md) — Build a specific TFM, not the bare csproj, for this single-project MAUI app
- [avoid-placeholder-gitkeep-trees](2026-06-18-avoid-placeholder-gitkeep-trees.md) — Create source directories with real files instead of committing placeholder trees
- [do-not-use-ui-showcase-as-shell-home](2026-06-18-do-not-use-ui-showcase-as-shell-home.md) — Keep the UI Library showcase off the authenticated Shell startup path
- [android-visual-state-target-scope](2026-06-23-android-visual-state-target-scope.md) — Cross-element VisualState TargetName setters can build successfully but crash when Android Shell creates a tab
- [maui-xaml-commandparameter-types](2026-06-25-maui-xaml-commandparameter-types.md) � XAML CommandParameter literals are strings and can crash typed CommunityToolkit commands at runtime

- [android-defer-session-restore](2026-07-06-android-defer-session-restore.md) - Defer stored-token restore until after the startup page loads to avoid Android ANRs
- [ef-retry-strategy-manual-transactions](2026-07-21-ef-retry-strategy-manual-transactions.md) - EnableRetryOnFailure rejects bare BeginTransactionAsync; wrap transactions in a retry-idempotent execution-strategy delegate
- [shared-sql-fixture-unique-test-data](2026-07-26-shared-sql-fixture-unique-test-data.md) - Shared SQL fixtures require scenario-unique business keys so full-suite results match isolated runs
- [maui-ios-ci-publish-pitfalls](2026-07-25-maui-ios-ci-publish-pitfalls.md) - CI iOS publish needs macos-26 + maui-ios workload + a csproj-scoped TFM conditional; never override TargetFrameworks globally
- [android-jarsigner-strict-upload-key](2026-07-27-android-jarsigner-strict-upload-key.md) - Allow only jarsigner strict status 4 for a pinned self-signed Android upload certificate
- [google-play-service-account-permissions](2026-07-27-google-play-service-account-permissions.md) - Google Cloud IAM does not grant Play Console publishing access; authorize the CI account in both systems
- [maui-staticresource-keys-crash-at-launch](2026-07-28-maui-staticresource-keys-crash-at-launch.md) - Undefined StaticResource keys build clean and crash at page inflation; on the startup page that is a TestFlight launch crash
- [sync-upsert-must-merge-human-links](2026-07-29-sync-upsert-must-merge-human-links.md) - The recurring Pickup Pal import must coalesce PlayerProfileId, never null it — it kept wiping admin Match links within a minute
- [profile-merge-must-repoint-every-fk](2026-07-30-profile-merge-must-repoint-every-fk.md) - Profile merges must re-point every profile FK (CaptainPlayerProfileId was missed) and workflow buttons must show disabled, not hidden
- [profile-merge-feedback-final-keys](2026-07-30-profile-merge-feedback-final-keys.md) - Reconcile complete post-merge rating/like keys and soft-delete self-feedback or collisions
