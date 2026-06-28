# M7 RSVP and waitlist boundary

- Waitlisted state lives in `WaitlistEntries`; `RsvpResponses.Status` remains only `Going`, `Maybe`, or `NotGoing`.
- RSVP capacity, cancellation, promotion, check-in, and no-show writes are owned by `IRsvpRepository` and implemented with serializable SQL transactions composed through EF Core's execution strategy plus bounded retry-to-409 for SQL concurrency conflicts.
- RSVP eligibility checks current waiver acceptance and calls `IPaymentEligibilityService`; until M5 resumes, the default payment provider is explicitly deferred and must not create database-authoritative payment state.
- Player RSVP mutation endpoints require `Idempotency-Key`; replay is persisted through `IdempotencyKeys` with request hash, response status/body JSON, and completion timestamp so duplicate requests return the original response.
- Waitlist promotion persists a `PlayerWaitlistPromoted` outbox message in the same promotion transaction; M10 owns delivery/notification dispatch.
