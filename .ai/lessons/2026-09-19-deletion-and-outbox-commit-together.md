---
name: deletion-and-outbox-commit-together
description: Commit local deletion and upstream deletion intent in one retry-safe SQL transaction
area: data
created: 2026-09-19
---

**Context:** Account deletion removed local data before recording the optional Pickup Pal deletion.

**Problem:** A crash between the local commit and outbox save permanently lost the external
deletion request. Retry could no longer find the profile through the normal soft-delete filter.

**Resolution:** Record the idempotent deletion/audit intent inside the same execution-strategy
transaction as profile deletion, Identity anonymization, and token revocation. Recover identifiers
from the soft-deleted profile during replay. Call Pickup Pal only after commit.

**Takeaway:** Saving an outbox row before the network call is insufficient: it must commit with
the local mutation it represents. Failure-inject after the outbox save but before commit against
SQL with retries enabled, and verify rollback plus repeated committed replay.

Related: [[2026-07-21-ef-retry-strategy-manual-transactions]], [[m13-onboarding-backend]]
