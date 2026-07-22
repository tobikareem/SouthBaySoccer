# Session cancel and delete semantics

Session cancellation and deletion are deliberately different operations:

- Cancel/disable sets `SessionStatus.Canceled`, keeps the session visible in admin and player
  upcoming-session queries, shows a danger placard reading `Session has been cancelled`, and blocks
  RSVP at the Application boundary.
- Delete calls the repository soft-delete operation, preserving audit history while EF global query
  filters remove the session from ordinary admin/player reads.
- Both endpoints require `CanManageSessions`. The MAUI delete action requires explicit confirmation.

Canceled sessions may appear in chronological lists but must not become the featured next active
match. No schema migration is required because both `Status` and `BaseEntity.IsDeleted` already exist.
