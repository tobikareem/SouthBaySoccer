---
name: maui-ios-ci-publish-pitfalls
description: CI publish of the MAUI iOS app needs macos-26 + maui-ios workload + a csproj-scoped TFM conditional — three pitfalls that never show locally
area: tooling
created: 2026-07-25
---

**Context:** Standing up the `release/ios` TestFlight workflow (`.github/workflows/release-ios.yml`).
Locally `dotnet publish -f net10.0-ios` just works; the first four CI runs each failed differently.

**Problem:** Three failures that a fully-provisioned dev Mac can never reproduce:
1. iOS workload set 10.0.302 requires Xcode 26.6, which only exists on the `macos-26` runner image
   (`macos-15` tops out at 26.3).
2. Installing the bare `ios` workload isn't enough for a `UseMaui` project — restore fails with a
   misleading "install maui-tizen" error. The MAUI build packs come from `maui-ios`.
3. Restoring the multi-target csproj (`android;ios;maccatalyst`) demands ALL platform workloads even
   for `-f net10.0-ios`. Passing `-p:TargetFrameworks=net10.0-ios` is NOT the fix: global properties
   propagate to referenced projects and corrupted the `net10.0` Contracts restore
   ("Assets file … doesn't have a target for 'net10.0'").

**Resolution:** Pin `runs-on: macos-26`; install `maui-ios --version <workload-set>`; add a scoped
conditional inside `SouthBaySoccer.csproj` —
`<TargetFrameworks Condition="'$(PublishIosOnly)' == 'true'">net10.0-ios</TargetFrameworks>` — and
pass `-p:PublishIosOnly=true`, so only the MAUI project narrows its TFMs.

**Takeaway:** Never override `TargetFrameworks` from the command line in a multi-project build —
flip a project-scoped conditional instead. For MAUI CI, install the `maui-<platform>` workload (not
the bare platform one) pinned to a workload set, and choose the runner image by the Xcode version the
workload set requires (check `actions/runner-images` READMEs). Validate fixes with a full local
publish using the exact CI flags before burning runner minutes.

Related: [[maui-build-target-framework]]
