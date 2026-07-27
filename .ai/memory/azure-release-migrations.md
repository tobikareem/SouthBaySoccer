# Azure release migration boundary

The production Functions workflow deploys only from `release/azure`. It validates the backend,
applies pending EF Core migrations as a protected deployment step with a dedicated SQL migration
credential, and deploys the Function App only after migration succeeds. Function cold start must
never apply migrations. Migrations in this automatic pipeline must be backward-compatible expand
changes; destructive contract migrations require a separate reviewed rollout.
