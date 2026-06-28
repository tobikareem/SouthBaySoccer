# M6 Scheduling

M5 Stripe payments are intentionally deferred while M6 scheduling is implemented. Do not add
temporary database-authoritative payment eligibility logic; Stripe remains the future source of
truth when M5 resumes.

M6 scheduling uses `Season`, `Venue`, `RecurrenceRule`, and `Session` as first-class backend
entities. Session times are stored as UTC. Recurring occurrence creation uses a deterministic
occurrence key formatted from recurrence rule id plus UTC start time, and replayed creation returns
the existing session instead of creating a duplicate.

Cancellation currently marks the session canceled. Notification fan-out is deferred to the M10
outbox dispatcher rather than being implemented as direct sends in M6.
