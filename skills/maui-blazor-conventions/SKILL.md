---
name: maui-blazor-conventions
description: Use when writing, scaffolding, reviewing, or refactoring C#/.NET code for the Pickup Soccer app — .NET MAUI Blazor Hybrid (mobile), Blazor Web App (website), ASP.NET Core Web API (backend), EF Core models, Stripe integration, or shared Razor components. Trigger on any request to "create a component/page/service", "add an endpoint", "model the data", "wire up Stripe", or similar .NET work in this project.
---

# Pickup Soccer — .NET MAUI / Blazor Conventions

Architecture: one solution, shared UI, three heads — **MAUI Blazor Hybrid** (mobile app), **Blazor Web App** (website), **ASP.NET Core Web API** (backend). Razor components are shared via a Razor Class Library. Target the current LTS .NET. Follow standard Microsoft .NET conventions plus the project specifics below. When unsure of an API, check Microsoft Learn before guessing.

## Solution Structure

```
PickupSoccer.sln
 ├─ src/
 │   ├─ PickupSoccer.Shared/        # Razor Class Library: shared components, view models, DTOs
 │   ├─ PickupSoccer.Mobile/        # .NET MAUI Blazor Hybrid app
 │   ├─ PickupSoccer.Web/           # Blazor Web App (website)
 │   ├─ PickupSoccer.Api/           # ASP.NET Core Web API
 │   ├─ PickupSoccer.Core/          # Domain entities, interfaces, business logic (no framework deps)
 │   └─ PickupSoccer.Infrastructure/# EF Core DbContext, repositories, Stripe, external services
 └─ tests/
     ├─ PickupSoccer.Core.Tests/
     └─ PickupSoccer.Api.Tests/
```

Keep `Core` free of framework/IO dependencies. `Infrastructure` depends on `Core`. Heads (`Mobile`/`Web`/`Api`) depend inward only.

## Naming & Style

- PascalCase for types, methods, properties, constants; camelCase for locals/params; `_camelCase` for private fields; `I`-prefix interfaces.
- One public type per file; file name matches type. Razor components in PascalCase (`PlayerCard.razor`).
- `async`/`await` end-to-end; suffix async methods with `Async`; accept and pass a `CancellationToken`. Never `.Result`/`.Wait()`.
- Prefer records for DTOs and immutable data; `required` and nullable reference types enabled.
- Use `var` when the type is obvious; explicit type otherwise. Enable nullable + treat warnings as errors in CI.

## Razor Components

- Keep components small and single-purpose; `@code` block at the bottom, or code-behind (`Foo.razor.cs`) when logic grows.
- Parameters: `[Parameter] public required string PlayerId { get; set; }`. Cascade auth/state via `CascadingParameter`.
- No business logic in components — call injected services. UI state only.
- Shared components live in `PickupSoccer.Shared` so Mobile and Web reuse them. Platform-specific bits go behind an interface (`IDeviceService`) implemented per head.
- Use `EditForm` + `DataAnnotationsValidator` for forms.

## Dependency Injection & Services

- Register services in each head's `Program.cs` / `MauiProgram.cs`. Scoped for per-request/per-circuit, Singleton for stateless clients, Transient for lightweight helpers.
- Talk to data through interfaces in `Core` (e.g., `IPlayerRepository`, `IPaymentService`) implemented in `Infrastructure`.
- Use `IHttpClientFactory` / typed clients for the API; never `new HttpClient()`.
- Use `IOptions<T>` for configuration; never read `IConfiguration` deep in the stack. Secrets (Stripe keys, connection strings) come from user-secrets in dev and a secret store in prod — never commit them.

## Data Model (EF Core, PostgreSQL)

Core entities reflect the product: `Player`, `Membership`, `Event` (session), `Rsvp`, `Match`, `MatchStatLine`, `Payment`, `Waiver`, `AdminRole`.

- Use `Guid` keys, UTC `DateTimeOffset` timestamps, and soft-delete (`IsDeleted`) where history matters.
- Configure with `IEntityTypeConfiguration<T>` classes, not data annotations on entities.
- Migrations live in `Infrastructure`; never edit applied migrations — add new ones.
- Query async with projection to DTOs (`.Select(...)`) — avoid returning entities to the UI.

## Stripe Integration

- Server-side only. The API owns all Stripe calls; the app/website never holds secret keys.
- Subscriptions for monthly dues; one-time PaymentIntents for guest drop-ins.
- **Webhooks are the source of truth** for payment status. Verify the signature, handle events idempotently (store processed event IDs), and update `Membership`/`Payment` from events like `invoice.paid`, `invoice.payment_failed`, `customer.subscription.deleted` — never trust the client.
- Keep a clean `IPaymentService` abstraction in `Core`; the Stripe SDK stays in `Infrastructure`.
- Consider mobile app-store policy: route purchases through Stripe Checkout / the website to avoid in-app-purchase requirements (real-world services).

## API Design

- RESTful controllers or minimal APIs grouped by resource; version under `/api/v1/`.
- DTOs in/out (never entities); validate with FluentValidation or DataAnnotations; return `ProblemDetails` on error.
- AuthN/AuthZ via ASP.NET Core Identity or Entra External ID + JWT bearer; role-based policies (`Owner`, `GameAdmin`, `Captain`, `Player`).
- Paginate list endpoints; return 201 with location on create.

## Testing & Quality

- xUnit + FluentAssertions; mock with NSubstitute/Moq. Unit-test `Core` logic and stat calculations; integration-test the API with `WebApplicationFactory` and a test PostgreSQL (Testcontainers).
- Add a test for every bug fix. Keep `Core` at high coverage.
- Run `dotnet format` + analyzers in CI; warnings are errors.

## Checklist for new code

1. Lives in the right project (UI vs Core vs Infrastructure)?
2. Async with `CancellationToken`, nullable-aware, no secrets in source?
3. Talks to data via a `Core` interface, not EF directly from the UI?
4. DTOs at the boundary, validation present?
5. Stripe state driven by verified, idempotent webhooks?
6. Tests added/updated?
