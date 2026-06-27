# M1 Operational Records

M1.3 keeps refresh tokens, processed webhooks, and outbox rows as immutable operational records.
Refresh tokens store only hashes and metadata needed for rotation, reuse detection, and family
revocation. `ProcessedWebhookEvents` and `OutboxMessages` use SQL uniqueness for replay/idempotency.
These tables are not soft-deleted; ordinary EF hard deletes are blocked by the audit interceptor.
