---
name: profile-merge-must-repoint-every-fk
description: ReassignProfileStatsAsync missed MatchTeam.CaptainPlayerProfileId — a merged captain silently broke preselection and hid the Lock button
area: data
created: 2026-07-30
---

**Context:** A player claim-merge (duplicate profile → claimed profile) re-points stats rows via
`StatsRepository.ReassignProfileStatsAsync`.

**Problem:** The reassign covered TeamAssignments, PlayerMatchStats, MatchEvents, votes, likes,
awards, and corrections — but not `MatchTeam.CaptainPlayerProfileId`. After merging a captain
(Desire Asinya), the team still pointed at the retired profile: the captain checkbox could no longer
preselect (2 of 3 shown), and `CanLockTeams` failed its captain-has-assignment check, hiding the
Lock button with no explanation. Compounding UX: the Lock button was `IsVisible={CanLockTeams}`, so
when it mattered most it simply wasn't there.

**Resolution:** (1) Reassign now re-points captaincy alongside the assignments (Infrastructure test
covers it). (2) The Lock button stays visible-but-disabled with a hint until lockable. (3) Existing
broken games heal by re-granting the captains (rebuilds the team topology). (4) The Game Day
"Confirm result and goals" row now mirrors the post-game screen's real gate, including the
admin-on-lockable-draft auto-lock path.

**Takeaway:** A profile merge must re-point EVERY foreign key that references profiles — grep the
schema for `PlayerProfileId` (including nullable and role-specific columns like
`CaptainPlayerProfileId`) whenever adding one, and add it to the reassign. And never hide the only
button that completes a workflow behind its own enablement condition; show it disabled with the
reason.

Related: [[sync-upsert-must-merge-human-links]]
