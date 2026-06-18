# SouthBaySoccer

SouthBaySoccer is the operating application for paid pickup soccer sessions in the South Bay
community. It is being developed to manage players, memberships, waivers, recurring sessions,
RSVP capacity and waitlists, game-day check-in, team assignment, payments, and player statistics.

The repository currently contains an existing .NET MAUI sample application that is being migrated
incrementally into the soccer domain, alongside the initial backend and test project structure.

## Architecture

- **Client:** .NET 10 MAUI single-project application using XAML, Shell, and MVVM.
- **Backend:** .NET 10 Azure Functions v4 using the isolated worker model.
- **Application:** use cases, validation, and external-service abstractions.
- **Domain:** entities, value objects, domain rules, events, and repository interfaces.
- **Infrastructure:** EF Core 10, Azure SQL, Identity, Stripe, messaging, maps, and storage adapters.
- **Contracts:** transport-safe request, response, and shared API models.
- **Testing:** xUnit, FluentAssertions, and Moq.

The backend follows Clean Architecture:

```text
Domain <- Application <- Infrastructure
                 ^              ^
                 |              |
                 +--- Functions-+
```

See [documentation/architecture.md](documentation/architecture.md) for the complete architecture,
security, persistence, payment, reliability, deployment, and testing decisions.

For mobile UI implementation, [documentation/mobile-wireframes.html](documentation/mobile-wireframes.html)
is the authoritative visual and interaction reference. The reusable MAUI implementation contract is
defined in [_specs/client-ui.md](_specs/client-ui.md).

## Repository structure

```text
SouthBaySoccer.slnx
├── SouthBaySoccer/                    # Existing .NET MAUI client
├── src/
│   ├── SouthBaySoccer.Contracts/
│   ├── SouthBaySoccer.Domain/
│   ├── SouthBaySoccer.Application/
│   ├── SouthBaySoccer.Infrastructure/
│   └── SouthBaySoccer.Functions/
├── tests/
│   ├── SouthBaySoccer.Domain.Tests/
│   ├── SouthBaySoccer.Application.Tests/
│   ├── SouthBaySoccer.Infrastructure.Tests/
│   ├── SouthBaySoccer.Functions.Tests/
│   └── SouthBaySoccer.Client.Tests/
├── documentation/
├── _specs/                            # Requirements, design, and ordered tasks
├── skills/
└── .ai/                               # Shared agent memory and lessons
```

## Prerequisites

- .NET 10 SDK
- Visual Studio 2022 with the .NET MAUI workload for client development
- Azure Functions Core Tools when running the Functions project locally
- A SQL Server or Azure SQL-compatible database when persistence is configured
- Azurite when local Azure Storage emulation is required

## Build

Build the backend projects:

```powershell
dotnet build .\src\SouthBaySoccer.Domain\SouthBaySoccer.Domain.csproj
dotnet build .\src\SouthBaySoccer.Application\SouthBaySoccer.Application.csproj
dotnet build .\src\SouthBaySoccer.Infrastructure\SouthBaySoccer.Infrastructure.csproj
dotnet build .\src\SouthBaySoccer.Functions\SouthBaySoccer.Functions.csproj
```

Build the MAUI client for Windows:

```powershell
dotnet build .\SouthBaySoccer\SouthBaySoccer.csproj -f net10.0-windows10.0.19041.0
```

Use the target framework appropriate to platform-specific work:

- `net10.0-android`
- `net10.0-ios`
- `net10.0-maccatalyst`
- `net10.0-windows10.0.19041.0`

## Test

Run the relevant backend test projects:

```powershell
dotnet test .\tests\SouthBaySoccer.Domain.Tests\SouthBaySoccer.Domain.Tests.csproj
dotnet test .\tests\SouthBaySoccer.Application.Tests\SouthBaySoccer.Application.Tests.csproj
dotnet test .\tests\SouthBaySoccer.Infrastructure.Tests\SouthBaySoccer.Infrastructure.Tests.csproj
dotnet test .\tests\SouthBaySoccer.Functions.Tests\SouthBaySoccer.Functions.Tests.csproj
dotnet test .\tests\SouthBaySoccer.Client.Tests\SouthBaySoccer.Client.Tests.csproj
```

During development, prefer running the affected test project or individual test first.

## Local Functions configuration

Copy:

```text
src/SouthBaySoccer.Functions/local.settings.json.example
```

to:

```text
src/SouthBaySoccer.Functions/local.settings.json
```

Add local settings as backend capabilities are implemented. Never commit credentials, connection
strings, Stripe secrets, signing keys, personal data, or other sensitive configuration.

## Core engineering rules

- Stripe verified webhooks are the source of truth for payment state.
- The MAUI client never connects directly to Azure SQL or invokes privileged provider APIs.
- Domain entities use `Guid` identifiers and UTC timestamps.
- Mutable persisted entities use soft deletion.
- Validation belongs in the Application layer using FluentValidation.
- Large datasets must be filtered and paginated.
- Domain must not depend on Application, Infrastructure, Functions, EF Core, or the client.
- Introduce soccer features incrementally; do not wholesale-rewrite the existing MAUI sample.

## Agent guidance

Repository-wide coding-agent guidance is centralized at the root:

- [AGENTS.md](AGENTS.md) — Codex entry point and enforced project conventions
- [CLAUDE.md](CLAUDE.md) — Claude-specific entry point
- [_specs/](_specs/) — executable requirements, design, and implementation tasks
- [.ai/](.ai/) — durable project memory and lessons
- [skills/](skills/) — project-specific coding, branding, content, and statistics guidance

## Current status

The solution structure and initial abstractions are present and build successfully. The existing
MAUI project still contains sample project/task functionality. Backend features, concrete domain
entities, EF Core mappings, middleware, integrations, and meaningful automated tests are to be
implemented incrementally according to the architecture document.
