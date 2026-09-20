---
name: m16-group-membership
description: M16 / GRP-1 - group membership with approval: one PlayerGroupLink row per (player, group) moving through Pending/Approved/Declined/Removed; WhatsApp-listed players auto-approve; only approved members RSVP / waitlist / self check-in / claim; group admins manage members; owners (OwnerPhoneNumbers) appoint admins; nothing written to Pickup Pal
type: project
created: 2026-09-17
---

Spec: `_specs/stories/GRP-1-group-membership-approval/`. Product decisions (2026-09-16): many
groups per player; joining is a request a group admin approves, except a player Pickup Pal already
lists in that WhatsApp group is approved automatically; every group's games are visible to every
signed-in player but only approved members of a game's group can RSVP, join the waitlist, self
check-in, or claim; group admins approve / decline / remove members of their groups; only super
admins (the two owners) appoint or revoke group admins and add a known player directly; **all of it
lives in our database and is never written to Pickup Pal** (the `/linked` read is the only call).

## Rules

- **Record:** `PlayerGroupLink` (table `PlayerGroupLinks`, name kept) with `Status`
  (`GroupMembershipStatus` Pending | Approved | Declined | Removed | Withdrawn - Withdrawn is
  the player pulling their own pending request, so it never reads as an admin decline), `Role` (`GroupMemberRole`
  Member | Admin), `Source` (`GroupMembershipSource` WhatsApp | Request | SuperAdmin),
  `RequestedAtUtc`, `ApprovedAtUtc` / `ApprovedByPlayerProfileId`, `RemovedAtUtc` /
  `RemovedByPlayerProfileId`. **One row per (player, group)**: a later request *reactivates* a
  Declined / Removed row (stamps reset, `RequestedAtUtc` = now); the existing unique filtered index
  on `(PlayerProfileId, GroupChatId)` is the "unique active row" guarantee. Ending a membership
  clears `Role` and `IsPrimary`; the first approved membership becomes primary, and removing /
  leaving the primary group promotes the oldest remaining approved membership in the same save.
- **Approved is the only membership.** Repository reads that mean "member" are approved-only
  (`ListApprovedByPlayerAsync`, `ExistsApprovedAsync`, `FindLinkAsync`, `ListPlayerGroupsAsync`,
  `CountMembersAsync`, and the raw `PlayerGroupLinks` joins in `AnnouncementRepository` /
  `StatsRepository`); `ListByPlayerAsync`, `FindMembershipAsync`, `ListPlayerMembershipsAsync`,
  `ListGroupMembersAsync`, `CountByGroupAsync` see every status. Announcements audience,
  group leaderboards, the Game Day spectator pool, `SessionGroupResolver` (admin session default
  group), and the legacy `IsLinked` all mean Approved now.
- **State machine** lives only in `GroupMembershipService` (Application): `RequestAsync`
  (WhatsApp-listed -> Approved/WhatsApp, else Pending/Request; a Pending row is approved when
  WhatsApp now lists the player; otherwise idempotent for Pending / Approved rows; a Pickup Pal
  read failure just means "not listed"), `SeedFromWhatsAppAsync` (legacy `players/me/groups`
  read; only pairs with no row or a Pending row - an ended row (Declined / Removed / Withdrawn) is
  never touched, so an admin removal is never undone by a sign-in read), `AddDirectlyAsync` (owner;
  Approved/SuperAdmin, also approves a pending row and re-admits an ended one), `ApproveAsync` /
  `DeclineAsync` (Pending only), `WithdrawAsync` (self, Pending only), `RemoveAsync` (Approved
  only; leave = self), `SetRoleAsync` (Approved only). `DELETE players/me/memberships/{id}` is
  idempotent: Pending -> Withdrawn, Approved -> Removed, anything else is a no-op.
- **Gate:** `IGroupMembershipGate.EnsureCanJoinAsync(session, player)` runs in `SubmitRsvp`
  **for every submitted RSVP intent** (Going / Maybe / NotGoing), `SelfCheckIn`, and `ClaimParticipant`;
  `CancelRsvp`, admin check-in, admin RSVP override, and participant linking are
  not gated, so a removed member can cancel their existing spot while the RSVP window is open.
  An app-only session with no `GroupChatId` is open; an imported session missing its group is
  closed for joining. Waitlist promotion batches approved memberships inside the cancellation
  transaction and skips candidates whose membership ended. Failure -> `GroupMembershipRequiredException(groupName)` -> **403** with
  type `https://southbaysoccer/problems/group-membership-required`
  (`ProblemDetailsMapper.GroupMembershipRequiredProblemType`), detail names only the group.
- **Projection, never filtering:** `ResolveAccessAsync` adds `GroupChatId`, `GroupName`,
  `MembershipStatus`, `CanJoin` (no group, or Approved) to the feed (`SessionAdminResponse`,
  `CanJoinWaitlist` also requires `CanJoin`), the Game Day context (spectator Join requires
  `CanJoin`; `JoinBlockedReason` names the group), and the additive `SessionSummaryDto` /
  `SessionDetailDto` members the client will map.
- **Super admin = `PlayerRole.Owner`,** promoted at sign-in (`PickupPalUserSyncService`) and on
  `profiles/me` from the root Functions setting **`OwnerPhoneNumbers`** (comma-separated; same
  `+digits` normalization and hash comparison as `AdminPhoneNumbers`; a number in both lists is an
  owner; an owner number always wins over an existing role, and the promotion is **reversible**:
  an Owner whose number is no longer configured drops to GameAdmin (admin number) or Player at
  the next sign-in / `profiles/me`). The numbers live only in configuration. Token policies for
  Owner: `IsSuperAdmin`, `CanManageGroupMembers` (`AuthenticationPolicies` constants). **Role
  claims come from the token**, so a newly promoted (or demoted) owner needs a fresh sign-in
  before `IsInRole("Owner")` / `IsSuperAdmin` reflect it.
- **Authorization:** group-admin rights are per group, so `GET groups/{id}/members` and the
  approve / decline / remove routes declare `AuthenticatedPlayer` and the handler requires Owner or
  an Approved row with role Admin **for that group** (removing an admin needs Owner). Owner-only
  routes (`POST groups/{id}/members`, `PUT groups/{id}/admins`) declare `IsSuperAdmin` and the
  handler checks again. `GET players/search?q=` is Owner or admin of any group; `q` is a name
  fragment (2-64 chars, no `@`, fewer than 4 digits) and results carry masked phones only.
- **Routes:** `GET groups/catalog` (`GroupCatalogResponse`; pending counts only for groups the
  caller manages), `GET players/me/memberships`, `POST players/me/memberships/requests`,
  `DELETE players/me/memberships/{groupChatId}`, the members / review / add / admins routes above.
  Legacy `GET groups` (Pickup Pal catalogue, `AvailableGroupsResponse` - the MAUI client reads it,
  hence the new catalogue lives at `groups/catalog`), `GET players/me/groups`
  (`IsLinked` = has an Approved row) and `POST players/me/groups/link` (one-group request) keep
  their shapes.
- **Import attaches the group:** `PickupPalGame.GroupExternalId` (`[JsonIgnore]`, read from the
  game's `group.groupId`) is matched to `GroupChat.ExternalId` in `PickupPalGameImportService`;
  match -> `Session.GroupChatId`; an unknown id creates a local group from the game's display name
  and attaches the session (one group per external id per import pass). The id never lands on
  the snapshot and is never logged (see [[pickuppal-games-import]]). `CreatedByApp` sessions keep
  their own group.
- **Migration `AddGroupMembershipApproval`** (controlled deploy): fail-closed column defaults
  (Pending / Member / Request), backfill of every existing row to Approved / Member / WhatsApp with
  `RequestedAtUtc = ApprovedAtUtc = CreatedAt`, and a filtered `(GroupChatId, Status)` index.

## Deliberately out of scope

Admin notifications for pending requests, bans (a removed player may ask
again), Pickup Pal writes of any kind, and re-issuing tokens when a player becomes a group admin
(the check is per request in the handler, so no re-sign-in is needed).

## Client and catalogue follow-through

- MAUI group selection, My groups, member management, and owner tools are implemented (M16.7).
  Withdrawn requests reappear under Join another group. Name search rejects email/phone-like
  fragments before constructing an HTTP URL, in both the page model and API client.
- Nonmembers never see RSVP / join-waitlist buttons. Grouped responses require explicit Approved
  membership as well as CanJoin, and commands enforce the same restriction. Existing spots can
  only be released through a separate Cancel my spot button; capacity never blocks withdrawal.
- `groups/catalog` reconciles Pickup Pal's read-only all-groups list before returning local ids;
  outages fall back to stored groups. Discovery grants no membership.
- A conflicting membership batch or catalogue insert returns 409, not false success. Refresh and
  retry; do not swallow uniqueness conflicts that may roll back other rows in the same batch.

Related: [[pickuppal-groupchat-read-only]], [[pickuppal-phone-sign-in]], [[m15-pickuppal-game-creation]],
[[m14-pickuppal-roster-sync]], [[functions-pipeline-authz]], [[functions-problem-details]],
[[controlled-migrations]]
