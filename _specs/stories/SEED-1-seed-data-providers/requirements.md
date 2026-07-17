# SEED-1 — Seed-data providers

**Epic:** SEED — UI-first enabling work · **Milestone:** M11 · **Client story (enabling)**
**Applies:** `INV-13` (Font Awesome, no emoji), `NFR-Security` (no secrets/PII in the package),
`NFR-Migration` (each increment stays buildable) — see [`../../requirements.md`](../../requirements.md).
**Strategy source:** the UI-first seed-data strategy in [`../../design.md`](../../design.md) §12.

## Story

*As the* SouthBaySoccer app and development team, *I want* client-service interfaces backed by seed
implementations, *so that* every screen works before the backend (Function App, web services,
database) exists.

This is the **enabling** story for the UI-first phase. It introduces no product screen of its own;
it provides the data substrate the AUTH, SES, RSVP, STAT, LEAD, and PROF client screens bind to, so
those stories are validated in the client now and re-verified server-side when their backend
milestone lands. It does not re-decide the seed-data strategy — that lives in
[`../../design.md`](../../design.md) §12 — it specifies the concrete interfaces, fixtures, and DI
selection.

## Acceptance criteria

```gherkin
Scenario: A seed client returns deterministic fixtures matching the wireframe sample data
  Given the Seed provider is the active client implementation
  When a page model requests sessions, rosters, stats, leaderboards, or a profile
  Then the initial fixture state is fixed in code and identical on every run and every device
  And the values match the sample data shown in the mobile wireframes
  And no call performs network, file, or database access

Scenario: Seed commands change only the current in-memory demo state
  Given the Seed provider is active with its deterministic initial fixture state
  When a player changes RSVP intent, submits or confirms stats, or submits teammate ratings
  Then the matching Seed client updates an application-scoped in-memory state store
  And subsequent reads in that app run reflect the command
  And restarting or explicitly resetting the seed store restores the deterministic initial fixtures
  And no shared static fixture instance is mutated

Scenario: The active provider is selected by configuration
  Given the application is composed at startup
  When the configured data-source flag selects "Seed"
  Then the Seed client implementations are registered for every client-service interface
  And the page models and pages depend only on those interfaces
  When the flag selects "Api" before the complete typed API client set is available
  Then startup fails fast with a configuration error naming the missing registrations
  And M11.1 replaces that failure with the typed API client registrations

Scenario: Seeds are excluded from Release builds and carry no real personal data
  Given the client is compiled in the Release configuration
  Then the seed implementations are not compiled into the shipped package
  And selecting "Seed" is rejected by configuration validation
  And the seed fixtures contain only invented names, numbers, and identifiers
  And no real player phone number, email, address, or payment identifier is present

Scenario: Swapping a Seed client for the real typed API client requires no screen change
  Given a page model depends only on a client-service interface
  When a Seed client is replaced by its typed API client counterpart (M11.1)
  Then no page, page model, XAML, or binding is edited
  And only the dependency-injection registration changes

Scenario: SEED-1 supplies the complete client contract required by the first screen set
  Given SEED-1 is complete
  Then ISessionsClient supports the Sessions dashboard, session detail, and waitlist action
  And IRosterClient supports going and waitlist reads plus RSVP intent changes
  And IStatsClient supports match-stat reads, self-submit, captain confirmation, rateable teammates, and rating submission
  And ILeaderboardClient supports all four leaderboard metrics
  And IProfileClient returns identity, career stats, recent form, and pending confirmation copy
  And no dependent screen story adds another Seed client method or fixture

Scenario: A guest is represented in the seed roster fixtures
  Given the Seed roster fixtures are loaded
  Then a guest player "Tunde B." appears with a guest indicator
  And the guest carries stats like any other player
  And the guest has no account-linked identity
```

## Related stories

- [`AUTH-7`](../AUTH-7-welcome-back-screen/requirements.md) — consumes `IAuthenticationClient`; its
  seed stub simulates a successful Pickup Pal phone match so navigation works end-to-end.
- `SES`, `RSVP`, `STAT`, `LEAD`, `PROF` client screens — consume `ISessionsClient`, `IRosterClient`,
  `IStatsClient`, `ILeaderboardClient`, and `IProfileClient`; their backend-dependent scenarios are
  validated against these seeds in the UI-first phase.
- [`AUTH-4`](../../requirements.md) / `M11.1` — the typed API client that replaces these seeds with
  no screen change.
