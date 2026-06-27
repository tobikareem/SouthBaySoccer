# Controlled Migrations

EF Core migrations are the schema authority, but SouthBaySoccer applies them only as a controlled
deployment step with a deployment identity. The Function App startup must not call `Database.Migrate`,
`EnsureCreated`, or `dotnet ef database update`; a Functions test guards this rule.
