# M1 Audit and Soft-Delete Rule

M1.1 centralizes audit stamping and delete behavior in Infrastructure EF Core.
Mutable domain entities use global `IsDeleted == false` filters and EF `Deleted`
state is converted to `IsDeleted = true`. Immutable operational entities are not
soft-deleted, and ordinary EF hard deletes are blocked until an explicit
retention/purge service is implemented.
