# Sprint 02 - Backend Data Model Board

Living tracker for Sprint 02 backend data-model work. This sprint turns
[`../sprint-02-data-model.md`](../sprint-02-data-model.md) and
[`../sprint-02-data-flow.md`](../sprint-02-data-flow.md) into an implementation-ready persistence
plan before M1 entity and EF Core work starts.

**Status keys:** `To do` - `In progress` - `In review` - `Done` - `Blocked`.

## Sprint Goal

Define and validate the Azure SQL persistence foundation for the Function App backend: table
boundaries, single sources of truth, notification/alert dispatch tables, retention rules, migration
constraints, and implementation-ready Gherkin requirements.

## Snapshot

| Metric | Pts |
|--------|----:|
| Committed | 44 |
| Done | 44 |
| In progress | 0 |
| In review | 0 |
| To do | 0 |
| Blocked | 0 |

## Sprint Commitment

| Card | Story | Pts | Status | Depends on |
|------|-------|----:|--------|------------|
| Data model authority decisions | `DM-1` | 5 | Done | Sprint 02 data model draft |
| Azure SQL ERD and relationship flow | `DM-2` | 3 | Done | `DM-1` |
| Migration constraint checklist | `DM-3` | 5 | Done | `DM-1`, `DM-2` |
| EF Core schema creation and validation | `EF-1` | 5 | Done | `DM-3` |
| Audit and soft-delete infrastructure | `M1.1` | 5 | Done | `EF-1` |
| Notification and alert dispatch model | `NOTIF-4` | 5 | Done | `DM-2`, `EF-1` |
| Retention and purge policy baseline | `OPS-1` | 3 | Done | `DM-3`, `NOTIF-4` |
| EF implementation readiness package | `M1-READY` | 8 | Done | `DM-1`..`OPS-1` |
| Review and production-readiness gate | `REVIEW-1` | 5 | Done | all committed stories |

## Board

### To do

_(none)_

### In progress

_(none)_

### In review

_(none)_

### Done

| Card | Story | Evidence |
|------|-------|----------|
| Data model authority decisions | `DM-1` | `sprint-02-data-model.md` defines single authorities for waitlist, stats, MVP, enum storage, and audit actors. |
| Azure SQL ERD and relationship flow | `DM-2` | `sprint-02-data-model.md` contains the ERD and `sprint-02-data-flow.md` contains the relationship and notification/alert flows. |
| Migration constraint checklist | `DM-3` | The data model spec contains one authoritative migration checklist and Infrastructure schema contract tests validate core constraints. |
| EF Core schema creation and validation | `EF-1` | EF Core migrations, SQL Server-compatible Infrastructure tests, and no Function cold-start migration path are in place. |
| Audit and soft-delete infrastructure | `M1.1` | Save interceptor stamps audit fields from `IClock`/`ICurrentUser`; mutable deletes become soft deletes; immutable operational hard deletes are blocked; Infrastructure tests pass. |
| Notification and alert dispatch model | `NOTIF-4` | Outbox, notification message, recipient, delivery, alert rule, and alert instance tables and constraints are specified and mapped. |
| Retention and purge policy baseline | `OPS-1` | Retention table has concrete periods, owners, cadence, and production blockers for purge implementation. |
| EF implementation readiness package | `M1-READY` | Entity implementation order, migration readiness, schema validation evidence, and resolved RSVP waitlist authority are documented. |
| Review and production-readiness gate | `REVIEW-1` | DBA and staff-engineer addenda are recorded in the data model spec; remaining items are tracked as production blockers. |

### Blocked

_(none)_

## Requirements and Gherkin

### DM-1 - Data Model Authority Decisions

*As a* backend engineer, *I want* each business fact to have one persistence authority, *so that*
serializable transactions do not have to keep duplicate state in sync.

**Requirements**

- Waitlisted state is owned only by `WaitlistEntries`.
- `RsvpResponses` stores only Going, Maybe, and NotGoing.
- Goals and assists are owned by `MatchEvents`.
- `PlayerMatchStats` stores participation facts only.
- MVP is owned by explicit `MatchAwards`.
- Enums are persisted as strings, not integer ordinals.
- Audit writes support human and system actors.

```gherkin
Scenario: Waitlisted state is not duplicated in RSVP intent
  Given a player is placed on the waitlist for a session
  When the persistence model records that state
  Then a WaitlistEntry exists for the player and session
  And no RsvpResponse has Status = "Waitlisted"

Scenario: Promotion has a single state transition
  Given a player has a WaitlistEntry for a session
  When the player is promoted into the available capacity slot
  Then the WaitlistEntry is completed or removed from the active waitlist
  And one active RsvpResponse exists with Status = "Going"
  And both changes occur in the same serializable transaction

Scenario: Player match stats do not duplicate event totals
  Given a player scores a goal in a match
  When the match is persisted
  Then the goal is stored as a MatchEvent
  And PlayerMatchStats stores only participation facts
  And no goal or assist total is stored on PlayerMatchStats

Scenario: MVP authority is explicit
  Given rating votes exist for a match
  When the match MVP is recorded
  Then the selected MVP is stored as a MatchAward
  And PlayerRatingVotes are not the direct persistence authority for MVP

Scenario: System writes are auditable
  Given a Stripe webhook updates membership state
  When audit fields are stamped
  Then the actor type is "System"
  And the actor id identifies the webhook processing component
  And no fake PlayerProfile is required for the write
```

### DM-2 - Azure SQL ERD and Relationship Flow

*As a* product owner and engineer, *I want* diagrams of how the backend data relates, *so that* the
team can review the model before code and migrations are created.

**Requirements**

- ERD covers Identity/Profile, Waivers, Payments, Scheduling, RSVP, Match/Stats, Notifications,
  Alerts, and Audit.
- Flow diagram shows the business path from player profile to eligibility, RSVP, match day, stats,
  notifications, and alerts.
- Diagrams call out the single source of truth for waitlist, stats, and MVP.

```gherkin
Scenario: ERD covers all core areas
  Given the Sprint 02 ERD is opened
  Then it includes PlayerProfile as the business identity anchor
  And it includes payment, waiver, session, RSVP, match, stat, notification, alert, and audit tables
  And it does not anchor guest stats to AspNetUsers

Scenario: Flow diagram explains RSVP eligibility
  Given the Sprint 02 data flow diagram is opened
  When I inspect the eligibility flow
  Then waiver acceptance is required
  And either active membership or verified session drop-in payment is required
  And ineligible players are rejected before RSVP or waitlist writes

Scenario: Flow diagram separates notification concerns
  Given the notification flow is opened
  Then OutboxMessages are shown as transactional facts
  And NotificationMessages are shown as delivery intent
  And NotificationDeliveries are shown as provider attempts
```

### DM-3 - Migration Constraint Checklist

*As a* database implementer, *I want* one authoritative migration checklist, *so that* EF
configuration and SQL constraints cannot drift across duplicated spec sections.

**Requirements**

- One section is the authoritative migration checklist.
- Constraints include unique indexes, filtered indexes, rowversion columns, enum value storage,
  and check constraints.
- RSVP, waitlist, Stripe webhook, rating vote, notification, and alert idempotency risks are covered.

```gherkin
Scenario: RSVP constraints are migration requirements
  Given the migration checklist is used for implementation
  Then RsvpResponses has a filtered unique active index on SessionId and PlayerProfileId
  And RsvpResponses has a rowversion concurrency token
  And RsvpResponses rejects Status = "Waitlisted"

Scenario: Waitlist constraints are migration requirements
  Given the migration checklist is used for implementation
  Then WaitlistEntries has a filtered unique active index on SessionId and PlayerProfileId
  And WaitlistEntries has a filtered unique active index on SessionId and Position

Scenario: Stripe webhook idempotency is enforced in SQL
  Given Stripe sends the same event more than once
  When the webhook event is persisted
  Then ProcessedWebhookEvents has a non-filtered unique key on Provider and ProviderEventId
  And duplicate events cannot create duplicate ledger changes

Scenario: Rating votes are constrained
  Given a player rates another player in a match
  When the vote is persisted
  Then PlayerRatingVotes is unique by MatchId, VoterPlayerProfileId, and RatedPlayerProfileId
  And the score is constrained to 0 through 10
  And voter and rated player cannot be the same

Scenario: Enum persistence is stable
  Given an enum column is mapped by EF Core
  When the database value is stored
  Then the value is a bounded string
  And the value does not depend on enum integer ordering
```


### EF-1 - EF Core Schema Creation and Validation

*As a* backend engineer, *I want* Entity Framework Core to own schema creation and schema validation, *so that* Azure SQL is created from reviewed model configuration instead of ad hoc SQL or Function startup behavior.

**Requirements**

- EF Core migrations are the only application-owned schema creation path for Azure SQL.
- `EnsureCreated` is not used for production or integration schema creation.
- Function App cold start never applies migrations or creates schema.
- Each entity has an explicit `IEntityTypeConfiguration<T>` where schema behavior is nontrivial.
- EF configuration defines table names, keys, relationships, delete behavior, enum string conversion, max lengths, indexes, check constraints, and rowversion columns.
- Validation includes migration generation review and `Infrastructure.Tests` against SQL Server-compatible infrastructure.

```gherkin
Scenario: EF Core migrations create the schema
  Given M1 persistence implementation begins
  When the Azure SQL schema is created for the application
  Then schema creation is represented by EF Core migrations
  And the migration contains the table, relationship, index, check constraint, and rowversion rules from the authoritative checklist
  And no ad hoc SQL script is the primary schema authority

Scenario: Function cold start does not mutate schema
  Given the Function App starts in any environment
  When dependency injection and host startup run
  Then the app does not call Database.Migrate
  And the app does not call EnsureCreated
  And schema changes remain a controlled deployment step

Scenario: EF model configuration validates database shape
  Given entity configurations are implemented
  When the EF model is built
  Then enum properties use string conversions with bounded max lengths
  And delete behavior is explicit for required relationships
  And soft-delete query filters are applied only to mutable domain tables
  And immutable operational tables are excluded from soft-delete filters

Scenario: Infrastructure tests prove SQL-enforced constraints
  Given the migration has been applied to SQL Server-compatible test infrastructure
  When tests attempt duplicate RSVP, waitlist, webhook, rating vote, and notification recipient records
  Then the database rejects violations through configured unique or check constraints
  And rowversion concurrency behavior is verified for session and RSVP writes
```
### M1.1 - Audit and Soft Delete Infrastructure

*As a* backend engineer, *I want* EF Core to stamp audit fields and enforce soft-delete behavior centrally, *so that* domain entities are persisted consistently without application handlers duplicating infrastructure rules.

**Requirements**

- `SaveChanges` and `SaveChangesAsync` stamp `CreatedAt` and `CreatedBy` for added `BaseEntity` rows.
- `SaveChanges` and `SaveChangesAsync` stamp `UpdatedAt` and `UpdatedBy` for modified rows while preserving create audit fields.
- Mutable domain tables configured for soft delete convert EF `Deleted` state into `IsDeleted = true` updates.
- Soft-deleted mutable rows are hidden by global EF query filters.
- Immutable operational tables are not converted to soft delete; ordinary EF hard deletes are blocked until an explicit retention/purge service is implemented.
- Audit timestamps come from `IClock.UtcNow`; actor stamps come from `ICurrentUser.UserId` or the system actor fallback.

```gherkin
Scenario: Created audit fields are stamped centrally
  Given a new mutable domain entity is added to the DbContext
  When SaveChangesAsync runs
  Then CreatedAt is set from IClock.UtcNow
  And CreatedBy is set from ICurrentUser.UserId
  And UpdatedAt and UpdatedBy remain empty

Scenario: Updated audit fields preserve create audit
  Given an existing entity has create audit fields
  When the entity is modified and saved
  Then UpdatedAt is set from IClock.UtcNow
  And UpdatedBy is set from ICurrentUser.UserId
  And CreatedAt and CreatedBy are not overwritten

Scenario: Mutable deletes become soft deletes
  Given a mutable entity is removed through EF Core
  When SaveChangesAsync runs
  Then the row remains in the database with IsDeleted = true
  And normal EF queries no longer return the row

Scenario: Immutable operational rows cannot be accidentally hard-deleted
  Given an immutable operational entity is removed through ordinary EF Core tracking
  When SaveChangesAsync runs
  Then the save fails with a hard-delete protection error
  And the row remains in the database
```
### NOTIF-4 - Notification and Alert Dispatch Model

*As an* organizer, *I want* notifications and alerts to be durable and deduplicated, *so that*
players and admins receive the right messages without duplicate sends or lost failures.

**Requirements**

- `OutboxMessages` is separate from notification delivery tables.
- `NotificationMessages` stores logical message intent.
- `NotificationRecipients` snapshots recipient channel and destination.
- `NotificationDeliveries` stores provider delivery attempts.
- `AlertRules` define alert conditions.
- `AlertInstances` dedupe raised incidents and may create admin notifications.

```gherkin
Scenario: Domain event creates a durable notification
  Given a player is promoted from the waitlist
  When the promotion transaction commits
  Then an OutboxMessage is committed in the same transaction
  And a dispatcher can materialize one NotificationMessage from that outbox event

Scenario: Recipient destination is snapshotted
  Given a notification is prepared for a player
  When NotificationRecipients are created
  Then each recipient stores the channel
  And each recipient stores a destination hash and masked display value
  And later profile contact changes do not rewrite delivery history

Scenario: Duplicate sends are prevented
  Given the same notification is prepared twice for the same recipient and channel
  When NotificationRecipients are inserted
  Then a unique key on NotificationMessageId, Channel, and DestinationHash prevents duplicates

Scenario: Delivery attempts are retryable
  Given a provider send fails with a retryable error
  When the delivery attempt is recorded
  Then NotificationDeliveries stores the attempt number and sanitized failure category
  And NextRetryAtUtc is set
  And no raw provider payload or secret is stored

Scenario: Alert storm is deduplicated
  Given an outbox dispatcher is stuck
  When the alert rule fires repeatedly for the same condition
  Then one unresolved AlertInstance exists for the RuleId and DeduplicationKey
  And additional firings update the existing open alert instead of creating a storm
```

### OPS-1 - Retention and Purge Policy Baseline

*As an* operator, *I want* immutable operational tables to have concrete retention and purge rules,
*so that* replay protection and audit history survive long enough without unbounded table growth.

**Requirements**

- Retention periods are concrete before tables ship.
- Each purge family has an owner and cadence.
- Purge jobs are idempotent, paginated, observable, and excluded from Function cold start.
- Payment ledger and legal/audit tables are not automatically purged in MVP.

```gherkin
Scenario: Retention is concrete for operational tables
  Given an immutable operational table is added to the model
  Then its retention period is documented
  And its purge owner is documented
  And its purge cadence is documented

Scenario: Purge jobs do not break replay protection
  Given a purge job runs
  When it selects records for deletion
  Then it excludes records still needed for replay detection
  And it excludes records still needed for payment disputes
  And it emits observable completion and failure telemetry

Scenario: Payment ledger is retained for audit
  Given a PaymentLedgerEntry exists
  When automated purge jobs run
  Then the payment ledger row is not automatically purged during MVP
  And any archive is manual or legally controlled
```

### M1-READY - EF Implementation Readiness Package

*As a* developer starting M1, *I want* a clear implementation order and test map, *so that* entity
and EF configuration work can proceed without guessing.

**Requirements**

- Implementation order is documented.
- Current `RsvpStatus.Waitlisted` mismatch is called out before entity creation.
- Tests are mapped to Domain, Infrastructure, Application, and Functions projects.
- First migration readiness is defined before creating migration files.
- EF Core migrations are the schema creation mechanism.
- EF model and migration validation evidence is defined before implementation starts.

```gherkin
Scenario: Implementation order is explicit
  Given a developer starts M1 implementation
  Then the sprint board identifies the entity areas to implement in order
  And shared decisions are resolved before dependent entities are created

Scenario: RsvpStatus mismatch blocks implementation
  Given the current Domain enum still contains Waitlisted
  When RsvpResponse persistence is implemented
  Then the developer must remove Waitlisted from RSVP intent or split waitlist state into a separate enum
  And the database must continue using WaitlistEntries as the only waitlisted authority

Scenario: EF migrations are reviewed before schema creation
  Given the first EF Core migration is generated
  When it is reviewed against the Sprint 02 checklist
  Then every required table, relationship, index, check constraint, enum conversion, and rowversion column is present
  And no migration applies schema changes at Function cold start

Scenario: Infrastructure tests validate the migration checklist
  Given EF configurations are implemented
  When Infrastructure.Tests run against SQL Server-compatible infrastructure
  Then unique indexes, filtered indexes, check constraints, rowversion columns, and enum string conversions are verified
```

### REVIEW-1 - Review and Production-Readiness Gate

*As a* staff engineer, *I want* a formal review gate before implementation starts, *so that* the
model does not ship with known concurrency, security, or operational gaps.

**Requirements**

- DBA review findings are resolved or explicitly accepted as implementation blockers.
- Staff engineer review confirms the model can proceed to M1 implementation planning.
- Production blockers remain visible until tests, retention, and Azure SQL serverless behavior are
  proven.

```gherkin
Scenario: DBA review is incorporated
  Given the DBA review identifies must-fix constraints
  When Sprint 02 is closed
  Then each must-fix item is represented in the authoritative migration checklist
  And no duplicate checklist section competes with it

Scenario: Staff review distinguishes implementation-ready from production-ready
  Given the data model has passed design review
  When the staff engineer reviews the sprint
  Then the model may be approved for M1 implementation planning
  But production readiness remains blocked until high-risk tests and operational validation pass

Scenario: Production blockers are explicit
  Given Sprint 02 closes
  Then retention policies, sensitive-data minimization, SQL serverless latency behavior, and high-risk integration tests are listed as release blockers
```

## Definition of Done

- `sprint-02-data-model.md` and `sprint-02-data-flow.md` reflect all accepted authority decisions.
- The authoritative migration checklist is complete enough to drive EF configurations.
- EF Core migrations are explicitly documented as the schema creation path, with SQL-compatible tests validating the generated schema.
- Gherkin scenarios above are approved or moved into per-story specs before implementation.
- DBA and staff-engineer findings are recorded.
- M1 implementation follows the approved single-authority RSVP and waitlist decisions.

## Open Decisions

| Decision | Owner | Required before |
|----------|-------|-----------------|
| Confirm RSVP promotion transaction/retry shape | Backend lead | M7 RSVP implementation |
| Confirm single-MVP rule per match | Product + backend lead | MatchAwards configuration |
| Confirm one emergency contact or many | Product | EmergencyContacts configuration |
| Confirm SMS ships in v1 | Product | Notification channel implementation |
| Confirm Azure SQL serverless auto-pause policy | Operations | Production deployment |

## Review Evidence

- Data model spec: [`../sprint-02-data-model.md`](../sprint-02-data-model.md)
- Flow diagram: [`../sprint-02-data-flow.md`](../sprint-02-data-flow.md)
- DBA review: recorded in the data model addendum.
- Staff-engineer review: recorded in the data model addendum.

## How to Keep This Current

1. Move cards between columns as their requirements are approved.
2. Keep the snapshot point totals current.
3. If a Gherkin scenario changes, update the matching data-model requirement and vice versa.
4. When implementation begins, promote approved stories into `_specs/stories/<STORY-ID>/` if they
   need separate requirements/design/tasks files.
