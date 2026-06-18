---
name: maui-build-target-framework
description: Build a specific TFM, not the bare csproj, for this single-project MAUI app
area: build
created: 2026-06-17
---

**Context:** Building/verifying changes in the SouthBaySoccer .NET MAUI single-project app.

**Problem:** Running `dotnet build` without a target framework can fail or build the wrong/all heads, since the project multi-targets (windows, android, ios, maccatalyst).

**Resolution:** Build an explicit TFM, e.g. `dotnet build .\SouthBaySoccer.csproj -f net10.0-windows10.0.19041.0`, and pick the TFM that matches the change (use `net10.0-android` for Android-specific work).

**Takeaway:** Always pass `-f <tfm>` when building or verifying the MAUI client. Layer-specific test
projects now exist under `tests/`, but placeholder tests are not meaningful verification.
