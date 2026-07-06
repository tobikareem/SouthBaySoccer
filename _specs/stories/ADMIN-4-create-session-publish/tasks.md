# ADMIN-4 - Create and publish session tasks

- [x] **M11.ADMIN4a** Add `CreateSessionPage`/`CreateSessionPageModel` matching the `adminsession`
  wireframe, with date/time, venue, format, capacity, team count, preview, validation, and publish state.
- [x] **M11.ADMIN4b** Add `ISessionAdminClient` seed contract and resettable seed behavior that inserts
  a published session into the Sessions feed for RSVP.
- [x] **M11.ADMIN4c** Add admin navigation entry from Sessions/Admin actions and protect it behind seed
  `CanManageSessions` flags.
- [x] **M11.ADMIN4f** Add the admin created-sessions list and client/seed edit flow so existing sessions
  can be reopened and updated without duplicates.
- [ ] **M6.ADMIN4d** Implement backend create-draft, update-session, and publish-session use cases with
  FluentValidation, UTC storage, venue-local display, audit fields, and idempotent publish.
- [x] **TEST.ADMIN4e** Cover required-field validation, capacity/time validation, duplicate publish guard,
  unauthorized denial, post-publish visibility, and created-session listing/edit/update in the Sessions feed.
  _(Client/seed coverage complete; backend `CanManageSessions` server-side denial lands with M6.ADMIN4d.)_

**Done when:** an authorized admin can create a dated/location-based session, publish it to the team,
reopen created sessions for updates, and players can see updated RSVP-ready sessions without duplicate
feed cards.