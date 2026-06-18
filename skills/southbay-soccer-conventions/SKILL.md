---
name: southbay-soccer-conventions
description: Use when writing, scaffolding, reviewing, or refactoring SouthBaySoccer code across the .NET MAUI client, Azure Functions backend, Clean Architecture layers, EF Core/Azure SQL persistence, API contracts, or Stripe integration.
---

# SouthBaySoccer Solution Conventions

The authoritative architecture is `documentation/architecture.md`; executable requirements and
task order live in `_specs/`. Do not introduce Blazor, an ASP.NET Web API project, PostgreSQL, or
shared UI Razor components unless the architecture is explicitly changed first.

For MAUI UI work, `documentation/mobile-wireframes.html` is the authoritative visual and
interaction reference. `_specs/client-ui.md` defines the reusable token/style/control contract that
implements it. Update both together when product design changes.

## Solution boundaries

- `SouthBaySoccer/`: existing .NET 10 MAUI XAML/MVVM client, migrated incrementally.
- `SouthBaySoccer.Contracts`: transport-safe request/response models.
- `SouthBaySoccer.Domain`: entities, value objects, invariants, events, repository interfaces.
- `SouthBaySoccer.Application`: use cases, FluentValidation, ports, authorization requests.
- `SouthBaySoccer.Infrastructure`: EF Core 10, Azure SQL, Identity, Stripe and provider adapters.
- `SouthBaySoccer.Functions`: .NET 10 Azure Functions v4 isolated-worker composition/entry layer.

Dependency direction is Domain <- Application <- Infrastructure, with Functions composing
Application and Infrastructure. The MAUI client depends on Contracts, not Domain or Infrastructure.

## Core rules

- Use `Guid` entity keys and UTC `DateTime`; application/backend behavior obtains time through
  `IClock`.
- Mutable domain entities inherit `BaseEntity` and use soft deletion.
- Configure EF entities with `IEntityTypeConfiguration<T>` and apply global soft-delete filters.
- Use FluentValidation at Application boundaries; do not put data annotations on Domain entities.
- Keep Functions thin: translate triggers to use-case calls and responses; no business rules.
- Use typed `HttpClient` services in MAUI and pass stable IDs through Shell navigation.
- Never expose `IQueryable`; filter and paginate large datasets.
- Propagate `CancellationToken` through database, network, and provider I/O.
- Stripe verified, idempotent webhooks are the payment authority. Never trust client redirects.
- Never place secrets, tokens, connection strings, PII, or payment data in source, URLs, or logs.

## Testing

- xUnit + FluentAssertions + Moq.
- Domain tests are pure; Application tests mock ports; Infrastructure tests use SQL-compatible
  integration infrastructure; Functions tests cover middleware and transport behavior.
- Follow `MethodName_StateUnderTest_ExpectedBehavior` and Arrange/Act/Assert.
- Build the affected backend projects and a specific MAUI target framework.
