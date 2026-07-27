---
name: perf-baseline-and-telemetry
description: Perf baseline lives in _specs/perf/; prod SQL is serverless auto-pause; App Insights has requests only (no dependency telemetry)
type: environment
created: 2026-07-26
---

The production performance baseline, KQL queries, and the phased optimization plan live in
`_specs/perf/2026-07-performance-review.md` — in `_specs/`, not `documentation/`, because
`pages.yml` publishes all of `documentation/**` to public GitHub Pages. The prod database
(`SouthBaySoccerProdDb` on `southbaysoccer-prod-sql`) is **Standard S1 (20 DTU, 30GB max,
always-on) since 2026-07-26** — it was serverless GP_S_Gen5 with auto-pause (and the Azure SQL
free-limit offer, permanently opted out during the switch) before that, so telemetry from before
2026-07-26 carries 30–60s pause-resume tails that no longer exist; do not compare across that
boundary naively. Application Insights collects **request telemetry only, unsampled** (`host.json`
excludes Request from sampling); `AppDependencies` is empty because
`AddApplicationInsightsTelemetryWorkerService()` is never called.

**Why:** Perf work must not misattribute auto-pause resume time to code, must not measure SQL
round trips via App Insights (impossible today), and must not move perf reports into the publicly
published docs folder.

**How to apply:** Append baseline re-runs to `_specs/perf/2026-07-performance-review.md` using its
§2.4 KQL. Verify round-trip counts in tests (EF command interceptor / client request-count tests),
not App Insights. Keep `Request` excluded from sampling. Do not enable dependency/HTTP-client
instrumentation without the URL-redaction rule required by [[pickuppal-phone-sign-in]]. Put any
telemetry-bearing report in `_specs/perf/`, never `documentation/`.

Related: [[controlled-migrations]], [[game-day-today-projection]]
