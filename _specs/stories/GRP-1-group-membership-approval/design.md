# GRP-1 - Group membership with approval - Design

Realizes [`requirements.md`](requirements.md). Distilled rules for agents:
`.ai/memory/m16-group-membership.md`.

## Principles

1. **Our database owns membership.** `PlayerGroupLink` (table `PlayerGroupLinks`, name kept to
   limit churn) is the membership record. Pickup Pal is read only for catalogue discovery, the `/linked` cross-check
   (auto-approval) and for the import's group id; it is never written.
2. **One row per (player, group), moved through a lifecycle.** `Status` is Pending, Approved,
   Declined, Removed, or Withdrawn; a later request *reactivates* the same row (no history rows), so the
   existing unique filtered index on `(PlayerProfileId, GroupChatId)` where `IsDeleted = 0` is the
   "unique active row" guarantee. The audit stamps (`RequestedAtUtc`, `ApprovedAtUtc/By`,
   `RemovedAtUtc/By`, `UpdatedAt`) show the last decision.
3. **Approved is the only membership.** Every existing read that meant "member" - announcements
   audience and unread counts, group-scoped leaderboards, the Game Day spectator pool, the session
   default-group resolver, the legacy `IsLinked` - now means an Approved row. The repository
   encodes this: `ListApprovedByPlayerAsync`, `ExistsApprovedAsync`, `FindLinkAsync` (approved,
   no tracking), `ListPlayerGroupsAsync`, `CountMembersAsync` are approved-only;
   `ListByPlayerAsync`, `FindMembershipAsync` (tracked), `ListPlayerMembershipsAsync`,
   `ListGroupMembersAsync`, `CountByGroupAsync` see every status.
4. **Visibility is never gated; joining is.** Feeds, detail, and Game Day list every group's games
   and carry `GroupChatId`, `GroupName`, `MembershipStatus`, `CanJoin`. The gate runs only on the
   group-scoped writes (RSVP Going and the waitlist it may land on, self check-in, claim). Cancel
   is never gated; admin check-in, admin RSVP override, and participant linking are admin actions
   and are not gated.
5. **Super admin = `PlayerRole.Owner`,** promoted at sign-in (`PickupPalUserSyncService`) and on
   `profiles/me` (`GetMyProfileQueryHandler`) from the root Functions setting `OwnerPhoneNumbers`
   (same normalization and hash comparison as `AdminPhoneNumbers`; a number in both lists is an
   owner). The token then carries policies `IsSuperAdmin` and `CanManageGroupMembers`.
6. **Group-admin rights are per group and evaluated in the handler.** Policies are global, so
   endpoints a group admin may call declare `AuthenticatedPlayer` and the handler requires Owner
   or an Approved row with role Admin *for that group*. Owner-only endpoints declare `IsSuperAdmin`
   (pipeline fail-closed) and the handler checks again.

## Components

| Layer | Type | Responsibility |
|---|---|---|
| Domain | `GroupMembershipStatus`, `GroupMemberRole`, `GroupMembershipSource` | Lifecycle, role, and origin enums (stored as strings). |
| Domain | `PlayerGroupLink` (+ `Status`, `Role`, `Source`, `RequestedAtUtc`, `ApprovedAtUtc`, `ApprovedByPlayerProfileId`, `RemovedAtUtc`, `RemovedByPlayerProfileId`) | The membership record. Defaults are fail-closed (Pending / Member / Request). |
| Domain | `IPlayerGroupLinkRepository` (see principle 3), read models `PlayerMembershipReadModel`, `GroupMemberReadModel`, `GroupMembershipCounts` | Approved-only vs any-status reads, members view, counts. |
| Domain | `IGroupChatRepository.ListAllAsync`, `ListByExternalIdsAsync`; `IPlayerProfileRepository.SearchByDisplayNameAsync` | Catalogue, import matching, name search. |
| Application | `GroupMembershipService` | The state machine: `RequestAsync` (WhatsApp auto-approve vs Pending, idempotent, reactivates Declined/Removed), `SeedFromWhatsAppAsync` (legacy sign-in read; only pairs with no row or a Pending row), `AddDirectlyAsync`, `ApproveAsync`, `DeclineAsync`, `RemoveAsync`, `SetRoleAsync`. Primary-group bookkeeping lives here (first approved membership is primary; ending a membership clears it). |
| Application | `IGroupMembershipGate` / `GroupMembershipGate`, `SessionGroupAccess`, `GroupMembershipRequiredException` | `EnsureCanJoinAsync(session, player)` and `ResolveAccessAsync(sessions, player)`. |
| Application | `GroupMembershipAuthorization.IsSuperAdmin`, `GroupMembershipAccess` | Owner / per-group admin checks. |
| Application | Handlers: `GetGroupCatalog`, `GetMyGroupMemberships`, `RequestGroupMemberships`, `LeaveGroup`, `GetGroupMembers`, `ReviewGroupMember` (approve / decline / remove), `AddGroupMember`, `SetGroupAdmin`, `SearchPlayers`; validators `RequestGroupMembershipsCommandValidator`, `SearchPlayersQueryValidator` | Use cases. Legacy `GetMyGroups` / `LinkPlayerToGroup` now go through the service. |
| Application | `SubmitRsvpCommandHandler`, `SelfCheckInCommandHandler`, `ClaimParticipantCommandHandler` (gate), `ListUpcomingSessionsQueryHandler`, `GetTodayGameDayContextQueryHandler` (access projection), `SessionGroupResolver`, `GetSentAnnouncementsQueryHandler` (approved-only) | Enforcement and projections. |
| Application | `PickupPalGame.GroupExternalId` (`[JsonIgnore]`), `PickupPalGameImportService` | Attaches imported sessions to the persisted `GroupChat` by external id, creating a missing group from the sanitized game group name. Never clears an existing group; the id is not serialized onto the snapshot. Missing group identity fails closed in the membership gate. |
| Infrastructure | `AdminPhoneNumberOptions.OwnerPhoneNumbers`, `ConfiguredAdminPhoneNumberService`, `PickupPalUserSyncService`, `AuthenticationPolicyMapper` | Owner promotion and policies. |
| Infrastructure | `PlayerGroupLinkRepository`, `GroupChatRepository`, `PlayerProfileRepository`, `AnnouncementRepository` / `StatsRepository` (approved filter), EF configuration, migration `AddGroupMembershipApproval` | Persistence. |
| Infrastructure | `PickupPalGamesClient` | Reads `group.groupId` into `GroupExternalId`; never logs it. |
| Functions | `GroupMembershipFunctions`, `AuthenticationPolicies.IsSuperAdmin` / `CanManageGroupMembers`, `ProblemDetailsMapper.GroupMembershipRequiredProblemType`, DI | Transport. |
| Contracts | `Groups/GroupMembershipDtos.cs` (unchanged, committed in 2d87523); additive `MembershipStatus` / `CanJoin` on `SessionAdminResponse`; `GroupChatId` / `MembershipStatus` / `CanJoin` on `SessionSummaryDto`; `GroupChatId` / `GroupName` / `MembershipStatus` / `CanJoin` on `SessionDetailDto`; `GroupChatId` / `MembershipStatus` on `GameDayContextDto` | Wire shapes mapped by the MAUI client; nonmembers cannot see join controls. |

## Endpoints

| Method / route | Policy | Handler check | Response |
|---|---|---|---|
| `GET groups/catalog` | AuthenticatedPlayer | - (pending counts only for groups the caller manages) | `GroupCatalogResponse` |
| `GET players/me/memberships` | AuthenticatedPlayer | - | `MyGroupMembershipsResponse` |
| `POST players/me/memberships/requests` | AuthenticatedPlayer | - | `MyGroupMembershipsResponse` |
| `DELETE players/me/memberships/{groupChatId}` | AuthenticatedPlayer | - (Pending -> Withdrawn by self; Approved -> Removed by self) | `MyGroupMembershipsResponse` |
| `GET groups/{groupChatId}/members` | AuthenticatedPlayer | Owner or admin of that group, else 403 | `GroupMembersResponse` |
| `POST groups/{groupChatId}/members/{playerProfileId}/approve\|decline\|remove` | AuthenticatedPlayer | Owner or admin of that group; removing an admin needs Owner | `GroupMembersResponse` |
| `POST groups/{groupChatId}/members` | IsSuperAdmin | Owner | `GroupMembersResponse` |
| `PUT groups/{groupChatId}/admins` | IsSuperAdmin | Owner; target must be Approved (409 otherwise) | `GroupMembersResponse` |
| `GET players/search?q=` | AuthenticatedPlayer | Owner or admin of any group; `q` is a name fragment (2-64 chars, no `@`, fewer than 4 digits) | `PlayerSearchResponse` |

Legacy routes keep working: `GET groups` (Pickup Pal catalogue, `AvailableGroupsResponse` - the
MAUI client reads it, so the new catalogue shape lives at `groups/catalog`),
`GET players/me/groups` (`MyGroupsResponse`, `IsLinked` = has an Approved row), and
`POST players/me/groups/link` (one-group request through the same service).

Problem: `403` with type `https://southbaysoccer/problems/group-membership-required`, title
"Group membership required", detail "Only approved members of {GroupName} can join this game."

## State machine

```text
(no row) --request, not on WhatsApp--> Pending --approve (admin) / request or seed when now on WhatsApp--> Approved
(no row) --request, on WhatsApp / seed / owner add--> Approved
Pending  --decline (admin)--> Declined
Pending  --leave (self)--> Withdrawn
Approved --remove (admin) / leave (self)--> Removed
Declined | Removed | Withdrawn --request again / owner add--> Pending (or Approved when on WhatsApp / owner add)
Approved --set admin / revoke--> Approved (Role Admin | Member)
```

Ending a membership (Declined / Removed / Withdrawn) also resets `Role` to Member and `IsPrimary`
to false, and removing the primary group promotes the oldest remaining approved membership in the
same save; reactivating resets the approval / removal stamps and sets `RequestedAtUtc` to now.
`Withdrawn` is a distinct status (rather than reading `RemovedByPlayerProfileId == self`) so the
members view and the player's own list never show a self-withdrawal as an admin decline. The RSVP
gate applies to every submitted intent (Going / Maybe / NotGoing). A removed member may still
cancel an existing RSVP through DELETE while the RSVP window is open; cancellation creates no intent.
Owner promotion is reversible (an Owner whose number is no longer configured drops to GameAdmin
or Player); role claims come from the token, so a promotion takes effect at the next sign-in.

## Persistence

The authenticated catalogue refreshes group metadata from Pickup Pal's read-only all-groups
endpoint before projecting local group ids and membership counts. New groups therefore become
discoverable without a player first joining them through a legacy route. An unavailable provider
falls back to the persisted catalogue; cancellation propagates. Discovery never creates or changes
memberships and never removes a persisted group absent from an external response.

Membership batches remain idempotent when the requested state is already persisted. A save
conflict must propagate as HTTP 409: a competing write can roll back unrelated groups in the same
batch, so it cannot be treated as success. The caller can refresh and retry the full request.
Catalogue persistence conflicts likewise propagate rather than projecting uncommitted groups.

Imported sessions without a resolved local group are closed for joining and project `CanJoin = false`.
This includes legacy `pickuppal:` occurrence keys unless `PickupPalOrigin` is `CreatedByApp`.
Genuinely app-only sessions without a group stay open. Waitlist promotion queries approved
memberships for the candidate ids in one database query and applies the same unresolved-group rule.

Migration `AddGroupMembershipApproval` (controlled deploy): adds the eight columns with
fail-closed defaults (`Pending` / `Member` / `Request` / `0001-01-01`), backfills every existing
row to `Approved` / `Member` / `WhatsApp` with `RequestedAtUtc = ApprovedAtUtc = CreatedAt`, and
replaces the FK index on `GroupChatId` with a filtered `(GroupChatId, Status)` index for members
views and counts. The unique `(PlayerProfileId, GroupChatId)` and primary-group indexes are
unchanged.

## Test design

- Application: `GroupHandlerTests` (legacy read / link on the new service), `GroupMembershipHandlerTests`
  (request flow, idempotency, reactivation, leave, approve / decline / remove matrix incl. Owner,
  admin appointment rules, direct add, catalogue pending counts, search validation and masking),
  `GroupMembershipGateTests` (gate, access projection, RSVP / self check-in / claim rejection
  before any write, feed `CanJoin` and waitlist, snapshot JSON omits the group id),
  `ImportPickupPalGamesHandlerTests` (group attach / unknown group).
- Functions: `GroupMembershipEndpointMetadataTests` (every endpoint declares exactly one policy,
  Owner-only routes require `IsSuperAdmin`, the policy is granted to Owner only),
  `ProblemDetailsMapperTests` (403 + stable type).
- Infrastructure: `ConfiguredAdminPhoneNumberServiceTests` (owner list, independent of the admin
  list), `PickupPalUserSyncServiceTests` (Owner promotion - LocalDB), `SchemaContractTests`
  (columns and indexes - LocalDB).
