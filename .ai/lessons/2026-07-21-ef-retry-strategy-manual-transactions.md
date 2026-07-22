---
name: ef-retry-strategy-manual-transactions
description: EnableRetryOnFailure rejects bare BeginTransactionAsync; wrap in an execution strategy with a retry-idempotent delegate
area: data
created: 2026-07-21
---

**Context:** Running the MAUI app in Api mode against the deployed Functions API. Sign-in worked, but
`POST /api/auth/refresh` returned 500, so stored-session restore always fell back to the sign-in page.

**Problem:** `AddInfrastructure` configures `UseSqlServer(..., sql => sql.EnableRetryOnFailure())`, which
makes `SqlServerRetryingExecutionStrategy` the active strategy. `RefreshTokenExchangeService.RotateAsync`
called `dbContext.Database.BeginTransactionAsync(...)` directly, and EF throws
`InvalidOperationException: The configured execution strategy ... does not support user-initiated
transactions` at runtime. The Infrastructure test fixture does **not** enable retry-on-failure, so every
test passed while production failed. The exception middleware logs only the exception type (no message,
no exception object), which made the 500 opaque in App Insights.

**Resolution:** Wrap the transactional body in
`dbContext.Database.CreateExecutionStrategy().ExecuteAsync(...)` (same pattern as
`RsvpRepository.ExecuteInSerializableTransactionAsync`), with two retry-safety rules:
1. `dbContext.ChangeTracker.Clear()` at the start of the delegate so a re-run doesn't double-track
   entities added by a failed attempt.
2. Generate the replacement token id/secret **once, outside the delegate**. After an ambiguous commit
   (commit landed, strategy saw a transient failure and re-runs), the delegate re-reads its own consumed
   token; checking `ReplacedByRefreshTokenId == replacementId` lets it return its own committed rotation
   instead of misclassifying it as token reuse and revoking the entire token family.

**Takeaway:** With `EnableRetryOnFailure`, never call `BeginTransactionAsync` outside
`CreateExecutionStrategy().ExecuteAsync`, and write every strategy delegate to be safe to re-run:
reset tracked state on entry, and make any generated identity deterministic across attempts so a
post-ambiguous-commit retry can recognize work it already committed. Keep test fixtures' retry
configuration matched to production, or the failure mode is invisible to the suite.

Related: [[m1-operational-records]]
