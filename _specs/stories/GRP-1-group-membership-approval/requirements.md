# GRP-1 - Group membership with approval

**Epic:** GRP - **Milestone:** M16 - **Backend story**
**Applies:** `INV-11` (fail-closed), `NFR-Security` (no personal data in URLs or logs), the
group-chat read-only rule (our database owns player-to-group membership; Pickup Pal is read for the
`/linked` cross-check and never written), the import privacy rule in
`.ai/memory/pickuppal-games-import.md` (as refined by this story), and the Functions pipeline rules
in `.ai/memory/functions-pipeline-authz.md` / `functions-problem-details.md`.
**Builds on:** the M14 group link (`PlayerGroupLink`, `GET/POST players/me/groups*`),
[`SES-7`](../SES-7-pickup-pal-game-creation/requirements.md) (`Session.GroupChatId`),
[`RSVP-9`](../RSVP-9-pickup-pal-roster-sync/requirements.md) (RSVP handlers).

## Context

Until now a player "linked" themselves to a WhatsApp group with one tap, and every signed-in player
could RSVP on every game. Product decisions (2026-09-16): **(1)** a player can belong to many
groups; **(2)** joining a group is a request that a group admin approves, except that a player
Pickup Pal already lists in that WhatsApp group is approved automatically; **(3)** the games of
every group are visible to every signed-in player, but only approved members of a game's group can
RSVP, join the waitlist, self check-in, or claim a spot; **(4)** group admins approve, decline, and
remove members of their groups; **(5)** only the super admins - the two owners, identified by
phone numbers held in configuration and never in code or docs - can appoint or revoke group admins
and can add a known player straight into a group; **(6)** all of this lives in our database and is
never written to Pickup Pal.

## Story

*As a* player, *I want* to ask to join the groups I play with and see which games I may join,
*so that* I can RSVP with my group and still see what the other groups are up to.

*As a* group admin, *I want* to approve, decline, and remove members of my group, *so that* only
people who actually play with us take spots on our games.

*As an* owner, *I want* to appoint group admins and add a known player directly, *so that* the
groups run themselves without me approving every request.

## Acceptance criteria

```gherkin
Scenario: A player already in the WhatsApp group is approved at once
  Given I am signed in with a Pickup Pal user id
  And Pickup Pal lists me in the WhatsApp group "Bay Area Soccer"
  When I request membership in "Bay Area Soccer"
  Then my membership is Approved with source WhatsApp and no admin decision is needed
  And nothing is written to Pickup Pal

Scenario: A player not in the WhatsApp group waits for a group admin
  Given Pickup Pal does not list me in "Sunday League" (or the Pickup Pal read fails)
  When I request membership in "Sunday League"
  Then my membership is Pending with source Request
  And I can see every group's games but cannot RSVP on Sunday League's games

Scenario: Requests are idempotent and a removed member can ask again
  Given I already have a Pending or Approved row for a group
  When I request that group again
  Then nothing changes
  Given a group admin removed me (or declined me) earlier
  When I request that group again
  Then the same row becomes Pending again (or Approved when Pickup Pal lists me)

Scenario: Group-scoped actions require an approved membership
  Given a session that belongs to a WhatsApp group
  And I am not an approved member of that group
  When I RSVP Going, join the waitlist, self check-in, or claim a spot on it
  Then the request is refused with 403 and problem type
    "https://southbaysoccer/problems/group-membership-required" naming only the group
  And cancelling an RSVP I already hold is always allowed
  Given a session with no group
  Then all of those actions behave as before

Scenario: Feeds show every group's games with a join flag
  When I read the session feed, a session detail, or today's Game Day
  Then every group's games are listed
  And each carries GroupChatId, GroupName, my MembershipStatus, and CanJoin
  And CanJoin is true only when the session has no group or I am an approved member of it

Scenario: Group admins manage their own group's members
  Given I am an approved member with role Admin in "Bay Area Soccer"
  When I list the group's members
  Then I see pending requests and approved members
  When I approve, decline, or remove a member
  Then the row records who decided and when
  When I try the same on a group I do not administer
  Then the request is refused with 403
  And I cannot remove another group admin (only a super admin can)

Scenario: Only super admins appoint admins and add players directly
  Given my phone number is in the configured OwnerPhoneNumbers
  When I sign in
  Then my role is Owner and my token carries the IsSuperAdmin policy
  When I make an approved member of a group its admin, or revoke it
  Then the member's role changes
  When I try to make a pending (not approved) player an admin
  Then the request is refused with 409
  When I add a known player straight into a group
  Then the row is Approved with source SuperAdmin
  Given I am a group admin but not an owner
  When I try either of those
  Then the request is refused with 403

Scenario: Player search is name-only and masked
  Given I am an owner or administer at least one group
  When I search players by a name fragment of at least 2 characters
  Then I get at most 20 matches with masked phone numbers only
  When the fragment looks like a phone number or an email
  Then the request is refused with 400 (personal identifiers never travel in the query string)

Scenario: Imported games are attached to their group
  Given the Pickup Pal active-games feed reports a game whose group id matches a persisted GroupChat
  When the import runs
  Then the session's GroupChatId is set to that group
  And the group id is not persisted on the snapshot and never logged
  And a game whose group id matches nothing leaves the session's group unchanged

Scenario: Existing links survive the migration as approved WhatsApp memberships
  Given PlayerGroupLinks rows that existed before this story
  When migration AddGroupMembershipApproval runs
  Then each row is Approved, role Member, source WhatsApp, requested/approved at its CreatedAt
```

## Out of scope

The MAUI screens (join-groups picker, members list, owner tools), notifications to admins about
pending requests, and any Pickup Pal write. The legacy `GET groups` / `players/me/groups` /
`players/me/groups/link` endpoints keep their response shapes; the new shapes live on new routes.
