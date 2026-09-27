# Account deletion needs authentication, import, and client outcome boundaries

Deleting a profile and revoking refresh tokens does not revoke a signed access token. Validate the
local account's current active state after JWT validation; do not cache away deletion visibility.
Existing in-flight requests are a separate concurrency boundary.

A 401 is not evidence deletion committed. HTTP timeouts may follow a committed transaction and are
TaskCanceledException rather than HttpRequestException. Report uncertainty instead of success or
rollback; explain why a user must sign in again before clearing an unusable session.

If an upstream account remains intact, passive import must not recreate the deleted local profile.
Use account-specific deletion markers (not all soft-deleted profiles, since merges also soft-delete),
allow explicit registration, and batch suppression lookups. Inspect index filters: an IsDeleted=0
index cannot support the new IsDeleted=1 lookup. Add deleted-side indexes with a controlled migration.

An import may already hold a tracked profile when deletion commits. Do not use broad DbSet.Update
on that stale snapshot: it marks IsDeleted=false modified and can restore the row. Preserve EF's
property-level tracking, and exclude false IsDeleted updates on detached ordinary edits.

## Integration fixture correction

Windows CI exposed that the deletion-marker lookup test inserted profiles with IsDeleted=true.
The production audit interceptor resets IsDeleted=false for every Added entity, so the fixture
contained no deleted profiles and the lookup correctly returned nothing. Insert and save first,
then soft-delete and save; assert persisted fixture state before exercising deleted-row queries.
Compilation on macOS is not evidence a SQL fixture is valid; report Windows SQL execution separately.
