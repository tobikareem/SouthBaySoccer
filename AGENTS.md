# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## What this project is

SouthBaySoccer is the operating app for our **pickup soccer games** — the software that runs paid
pickup sessions for the South Bay group (and the future Pickup Soccer LLC). It exists to let
organizers collect dues, schedule recurring game days, manage who is coming (RSVP with capacity and
waitlists), gate play behind payment and signed waivers, run game-day check-in and team balancing,
and record core player stats.

Product/MVP scope, in priority order:
1. Auth, player profiles, role-based access (Owner/Admin, Game Admin, Captain, Player, Guest).
2. Stripe monthly subscriptions + one-time guest/drop-in payments. **Stripe is the source of truth
   for payment state** — sync via signed webhooks; never treat the database as the payment authority.
3. Sessions (game days) with RSVP intent states (Going / Maybe / Not Going / Waitlisted), capacity
   caps, automatic waitlist promotion, deadline locking, and separate check-in/attendance outcomes.
4. Digital waiver + code-of-conduct acceptance (timestamped) required before a player can RSVP.
5. Admin dashboard: paid/unpaid members, upcoming sessions, RSVP counts, attendance.
6. Game-day check-in, manual or skill/position-balanced team assignment, and basic stat recording
   (goals, assists, matches played, wins, MVP).

## Solution architecture

The system has two parts:

1. **Backend** — a .NET 10 Azure Functions v4 isolated-worker application built with **Clean
   Architecture**. This is the system of record: players, sessions, RSVPs, stats, payment ledger,
   Stripe webhook sync, and EF Core persistence.
2. **MAUI client** — the cross-platform app players and admins use. It is MVVM over the backend API.
   *(The current repo contents are the MAUI client, still carrying sample project/task scaffolding —
   see "Current state" below.)*

### Dependency rule (backend)

**Domain ← Application ← Infrastructure, with Functions as the composition root. Never invert.**

Each layer may depend only on layers to its left. Domain references nothing outside itself.
Application depends on Domain only. Infrastructure implements Application/Domain interfaces.
Functions wires dependencies and exposes triggers/middleware without containing business rules.
Domain must never reference Application, Infrastructure, or Functions namespaces.

### Backend folder layout

```
Domain/
├── Entities/
│   ├── Common/BaseEntity.cs              # Abstract base — all entities inherit this
│   ├── Identity/                         # players, admins, guests
│   ├── Scheduling/                       # game days / sessions, RSVPs
│   └── Stats/                            # match results, stat entries
├── Enumerations/                         # roles, RSVP states, payment status
└── Interfaces/Repositories/              # IRepository<T>, IUnitOfWork

Application/                              # use cases: commands/queries + handlers, DTOs, validators
Infrastructure/                          # EF Core DbContext, repository implementations, Stripe, email
Functions/                               # triggers, DI composition root, middleware
```

## Coding conventions

### Entity rules (ENFORCED — never deviate)

- **Primary keys**: `Guid` only — never `int` or auto-increment.
- **Timestamps**: UTC `DateTime` values only. Use `IClock.UtcNow` in application/backend behavior;
  never `DateTime.Now`. Convert to local time only at the UI boundary.
- **Soft deletes**: set `IsDeleted = true` — never call `DbSet.Remove()`.
- **Audit fields**: every entity inherits `BaseEntity`, which provides `Id`, `CreatedAt`,
  `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted`.
- **Nullable reference types**: enabled globally — respect nullability annotations; never suppress
  with `!` without a comment explaining why.

### C# style

- Explicit `using` directives at the top of each entity file — do not rely on implicit usings alone,
  as it hides dependencies. Add `using SouthBaySoccer.Domain.Entities.Common;` on all entity files
  and `using SouthBaySoccer.Domain.Enumerations;` on files that reference enums.
- Use `IReadOnlyList<T>` for collection return types from repositories — not `IEnumerable<T>`.
- Prefer pattern matching and expression-bodied members for simple computed properties.
- XML documentation (`/// <summary>`) on all public types and members in Domain.

### Validation

- Use **FluentValidation** in the Application layer — never data annotations on DTOs.
- Validate at the Application boundary (command/query handlers), not in domain entities.

### Repository pattern (backend)

- Interfaces in `Domain/Interfaces/Repositories/`; implementations in `Infrastructure/Repositories/`.
- Constrain `IRepository<T>` with `where T : BaseEntity` — not `where T : class`.
- Apply a global EF Core query filter (`IsDeleted == false`) in Infrastructure so soft-deleted
  records are automatically excluded.
- Never call `GetAllAsync()` on tables that grow large (e.g. `RsvpResponse`, `StatEntry`,
  `PaymentLedger`, `Match`) — always filter/paginate.

## Testing conventions

- **Framework**: xUnit + FluentAssertions + Moq.
- **Naming**: `MethodName_StateUnderTest_ExpectedBehavior`
  (e.g. `WinRate_WhenNoMatchesPlayed_ReturnsZero`).
- **Structure**: Arrange / Act / Assert with blank lines between sections.
- **Domain tests**: pure unit tests — no EF Core, no mocks for domain entities.
- **Application tests**: mock repository interfaces with Moq.
- Test computed properties with edge cases: null inputs, zero denominators, boundary dates.
- Run single tests during development; run the full suite before committing.

## Agent tooling (at the repo root)

This repo carries a shared, tool-neutral setup used by both Claude and Codex. It all lives at the
repo root alongside this file.

**Knowledge base — `.ai/`** (see `.ai/README.md`). Start each task by skimming `.ai/memory/INDEX.md`
and `.ai/lessons/INDEX.md` and reading any relevant entry. `memory/` = durable facts/conventions to
apply; `lessons/` = past problems not to repeat. Treat a recalled entry as background and verify any
file/symbol it names still exists. After finishing, add a memory (`/create-agent-memory`) for a
durable fact or a lesson (`/create-lessons`) for a non-obvious problem you solved.

**Mobile design source of truth — `documentation/mobile-wireframes.html`.** All MAUI product screens
and reusable controls must follow this wireframe's screen hierarchy, spacing, shapes, component
states, and navigation patterns. `_specs/client-ui.md` translates the wireframe into reusable MAUI
tokens/styles/controls. When they differ, update the spec and control library to match the wireframe;
do not introduce competing page-local patterns.

**Skills — `skills/`.** Invoke the relevant one before doing matching work:
- **`brand-design-kit`** — green/white Nigerian-flag brand (primary `#008751`); apply to any UI,
  doc, deck, or image.
- **`southbay-soccer-conventions`** — solution conventions for MAUI, Azure Functions, Clean
  Architecture, EF Core/Azure SQL, and Stripe webhooks.
- **`matchday-content`** — announcements, RSVP reminders, recaps.
- **`player-stats`** — goals/assists/matches-played, leaderboards, and league tables aligned with
  Premier League / UEFA Champions League conventions.

**Codex subagents — `.codex/agents/`**: `dotnet-code-reviewer` and `maui-xaml-reviewer` (read-only
reviewers), `test-writer` (drafts xUnit tests), `football-domain-modeler` (models the soccer domain).
**Codex skills — `.agents/skills/`**: `source-command-code-review` is the canonical automatic
workflow for "review my changes" and code-review requests.
**Codex prompts — `.codex/prompts/`**: `/create-lessons` and `/create-agent-memory`.

## Build & run

The MAUI client lives in `SouthBaySoccer/` (project folder, where the `.csproj` lives):

```powershell
dotnet restore .\SouthBaySoccer.csproj
dotnet build .\SouthBaySoccer.csproj -f net10.0-windows10.0.19041.0
```

Target frameworks for platform-specific work: `net10.0-android`, `net10.0-ios`,
`net10.0-maccatalyst`. Build the whole solution with `dotnet build` against the `.slnx` once backend
projects are added. Run tests with `dotnet test`.

## Workflow orchestration

1. **Plan before you code.** Enter plan mode for any non-trivial task (3+ steps, new entity, new
   endpoint, architectural change). Read the relevant spec in `_specs/` first. If implementation
   reveals the spec is ambiguous or stale, stop and resolve the spec before writing code.
2. **Use subagents** to keep the main context clean: offload exploratory research (reading large
   specs, tracing cross-layer dependencies) to a subagent, and run a code-review pass (the
   `source-command-code-review` skill or a reviewer subagent) after every implementation — not just
   when asked.
   One focused task per subagent.
3. **Self-improvement loop.** After any correction from the user, record the pattern in
   `.ai/lessons/`: what the mistake was, why it happened, and the rule that prevents it. Skim
   `.ai/lessons/INDEX.md` at the start of every task.
4. **Verify before done.** Never mark a task complete without proving it: `dotnet build` passes with
   zero warnings, relevant `dotnet test` passes, code review finds no critical issues, and no
   personal or payment data leaks into logs or URLs. Ask: *would a senior .NET engineer approve this
   PR?*
5. **Demand elegance (balanced).** For non-trivial work, pause before submitting: is there a simpler,
   more idiomatic .NET way? If a fix feels hacky or layers workarounds, implement the clean solution
   instead. Skip this for obvious, self-contained changes — don't over-engineer.
6. **Autonomous bug fixing.** Given a failing build, test failure, or bug report: just fix it. Point
   at the error, trace the cause, resolve it. If the root cause is in a spec, update the spec and the
   code together.

## Common mistakes to avoid

- **`DateTime.Now`** — use UTC; backend/application behavior obtains time through `IClock`.
- **`int` primary keys** — always `Guid`.
- **Hard deletes** — always set `IsDeleted = true`.
- **Missing `BaseEntity` inheritance** — every entity must inherit it.
- **Validation in entities** — belongs in FluentValidation validators in the Application layer.
- **Cross-layer imports** — Domain must never reference Application or Infrastructure namespaces.
- **`GetAllAsync()` on large tables** — `RsvpResponse`, `StatEntry`, `PaymentLedger`, and `Match`
  will grow; always filter.
- **DB as payment authority** — Stripe (via signed webhooks) is the source of truth for payment state.

## Core principles

- **Simplicity first** — make every change as small and focused as possible. Touch only the files the
  task requires. A clean diff is a sign of good work.
- **No laziness** — find root causes; never apply temporary fixes or workarounds that defer the real
  problem. Hold to senior-developer standards on every task.
- **Minimal impact** — changes should affect only what is necessary. Avoid side-effect edits,
  opportunistic refactors, or touching unrelated code — these introduce unreviewed risk.

## Current state of the code

The repo today is the **.NET MAUI sample project/task manager** (Projects, Tasks, Categories, Tags),
renamed to the `SouthBaySoccer` namespace. This is scaffolding to learn the MAUI patterns from and
**migrate incrementally** into the pickup-soccer domain — not the intended product. Do not do
unrelated wholesale rewrites; replace sample concepts feature by feature.

MAUI client patterns currently in use: MVVM with
CommunityToolkit.Mvvm (`[ObservableProperty]` / `[RelayCommand]`), Shell routes registered in
`MauiProgram.cs` via `AddTransientWithShellRoute`, page-model navigation params through
`IQueryAttributable`, and per-table repositories over raw `Microsoft.Data.Sqlite` with a lazy
`Init()`. As the backend comes online, the client's local SQLite repositories should give way to API
calls; the entity rules above govern the backend, not the legacy sample SQLite tables.
