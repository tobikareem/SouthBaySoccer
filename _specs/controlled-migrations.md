# Controlled Migration Deployment

This runbook closes M1.4 for the backend persistence foundation. EF Core migrations are the
application-owned schema authority, but they are applied outside Function App startup.

## Rule

The Function App must not call `Database.Migrate`, `EnsureCreated`, `dotnet ef database update`, or
any schema-mutating code during cold start. Scaled Functions instances can start concurrently, so
startup-time migrations can race, hold schema locks during request traffic, or run with the
application identity instead of a deployment identity.

## Deployment Identity

Use a dedicated deployment identity for migrations. It should have enough permission to apply the
reviewed migration scripts and no runtime access beyond the deployment step. The Function App
runtime identity should use least-privilege application permissions and should not be the schema
owner.

## Local and CI Validation

Before applying a migration to any shared environment:

```powershell
dotnet build src\SouthBaySoccer.Functions\SouthBaySoccer.Functions.csproj
dotnet test tests\SouthBaySoccer.Infrastructure.Tests\SouthBaySoccer.Infrastructure.Tests.csproj
```

The Infrastructure test fixture creates an isolated LocalDB database, applies all migrations with
`Database.MigrateAsync`, validates SQL constraints, and drops only databases whose name starts with
`SouthBaySoccer_Test_`.

## Script Generation

Generate a reviewed SQL script from the Infrastructure project:

```powershell
dotnet ef migrations script `
  --project src\SouthBaySoccer.Infrastructure\SouthBaySoccer.Infrastructure.csproj `
  --startup-project src\SouthBaySoccer.Infrastructure\SouthBaySoccer.Infrastructure.csproj `
  --context SouthBaySoccerDbContext `
  --idempotent `
  --output artifacts\migrations\SouthBaySoccer.sql
```

Review the script for:

- expected table and column changes only;
- filtered unique indexes;
- check constraints;
- rowversion columns;
- no raw secrets, tokens, provider payloads, or personal data;
- no destructive data movement without an approved rollback plan.

## Apply

Apply the reviewed script as a deployment stage before deploying or swapping Function App runtime
traffic. The stage should:

- use the deployment identity;
- run once per target database;
- log migration id, target database, commit SHA, start/end timestamps, and outcome;
- fail the deployment on SQL errors;
- never run from Function App startup.

## Rollback

Prefer forward-fix migrations for production. If a rollback is required, generate and review a
targeted rollback script for the exact source and target migration pair, then apply it with the same
deployment identity and logging rules.

## Verification

M1.4 is complete when:

- EF migration files exist under `src/SouthBaySoccer.Infrastructure/Persistence/Migrations`;
- SQL-backed Infrastructure tests pass against a migrated test database;
- Functions startup has a regression test proving it does not call migration APIs;
- `_specs/tasks.md` marks M1.4 complete.
