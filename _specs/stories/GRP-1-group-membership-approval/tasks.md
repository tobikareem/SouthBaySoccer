# GRP-1 - Group membership with approval - Tasks

GRP-1 is milestone **M16** (roadmap: [`../../tasks.md`](../../tasks.md)). Backend only; the MAUI
client is untouched (every new contract member is additive and the new shapes live on new routes).

- [x] **M16.1** Domain: `GroupMembershipStatus` / `GroupMemberRole` / `GroupMembershipSource`;
  the membership fields on `PlayerGroupLink`; approved-only vs any-status reads on
  `IPlayerGroupLinkRepository` (+ read models); `IGroupChatRepository.ListAllAsync` /
  `ListByExternalIdsAsync`; `IPlayerProfileRepository.SearchByDisplayNameAsync`.
  — Stories: `GRP-1` · Projects: Domain · Depends on: nothing.

- [x] **M16.2** Application: `GroupMembershipService` state machine; `IGroupMembershipGate` +
  `SessionGroupAccess` + `GroupMembershipRequiredException`; catalog / memberships / request /
  leave / members / review / add / set-admin / search handlers and validators; legacy
  `GetMyGroups` / `LinkPlayerToGroup` on the service; gate in RSVP, self check-in, claim; access
  projection in the feed and Game Day; approved-only `SessionGroupResolver` and sent-announcements
  scope; `PickupPalGame.GroupExternalId` (JsonIgnore) and the import's group attach.
  — Stories: `GRP-1` · Projects: Application · Depends on: M16.1.

- [x] **M16.3** Infrastructure: `OwnerPhoneNumbers` option and Owner promotion at sign-in and on
  `profiles/me`; `IsSuperAdmin` / `CanManageGroupMembers` policies for Owner; repository reads;
  approved filter on announcement and leaderboard queries; `groupId` on the games client; EF
  configuration; migration `AddGroupMembershipApproval` with the Approved/WhatsApp backfill.
  — Stories: `GRP-1` · Projects: Infrastructure · Depends on: M16.1.

- [x] **M16.4** Functions + Contracts: `GroupMembershipFunctions` (11 endpoints, fail-closed
  metadata); problem type `group-membership-required`; DI; additive `MembershipStatus` / `CanJoin`
  (+ group fields) on the feed, summary, detail, and Game Day DTOs.
  — Stories: `GRP-1` · Projects: Functions, Contracts · Depends on: M16.2, M16.3.

- [x] **M16.5** Tests per `design.md` "Test design" in Application, Functions, and Infrastructure
  (schema contract and Owner promotion are LocalDB-bound and run in CI); Domain and Client suites
  green without edits.
  — Stories: `GRP-1` · Depends on: M16.2–M16.4.

- [x] **M16.6** Knowledge base: `.ai/memory/m16-group-membership.md` + INDEX line; the privacy
  refinement in `.ai/memory/pickuppal-games-import.md`; story index row; M16 section in `tasks.md`.
  — Stories: `GRP-1` · Depends on: M16.5.

- [ ] **M16.7** MAUI: join-groups picker (`groups/catalog`, `players/me/memberships/requests`),
  view-only rendering off `CanJoin` / `MembershipStatus` and the `group-membership-required`
  problem type, members screen for group admins, owner tools (add member, appoint admin, search).
  — Stories: `GRP-1` · Projects: SouthBaySoccer · Depends on: M16.4.

- [ ] **M16.8** Configure `OwnerPhoneNumbers` in each Functions environment (root setting, the two
  owners' numbers; never committed) and confirm both owners see `IsSuperAdmin` after signing in.
  — Stories: `GRP-1` · Depends on: M16.3.

- [ ] **M16.9** Deploy: run migration `AddGroupMembershipApproval` through the release pipeline;
  verify existing links read as approved WhatsApp memberships and that an imported game shows its
  group.
  — Stories: `GRP-1` · Depends on: M16.4.

**Done when:** a player in the WhatsApp group is approved at once and anyone else waits for a
group admin, every group's games are visible but only approved members can RSVP / waitlist / self
check-in / claim (403 with the stable problem type otherwise), group admins manage their own
members, only the configured owners appoint admins or add players directly, nothing is written to
Pickup Pal, and the MAUI client keeps compiling and passing unchanged.
