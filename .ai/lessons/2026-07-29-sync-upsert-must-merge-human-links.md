---
name: sync-upsert-must-merge-human-links
description: A recurring import must merge, never blindly overwrite, fields other flows also write — it kept wiping admin Match links
area: data
created: 2026-07-29
---

**Context:** Game Day matching — an admin links an imported keyless Pickup Pal participant
("addguest tob8") to a real player profile via Match / self-claim.

**Problem:** The Pickup Pal import re-runs every ~1 minute (`GameDayPickupPalRefreshService`).
`ReplaceParticipantsAsync` upserted `row.PlayerProfileId = participant.PlayerProfileId` straight
from the re-resolved import, and a keyless participant always re-resolves to `null` — so every pass
silently undid the admin's link within a minute. The waitlist popup kept showing "tob8 — Not linked
to a profile" even though the link was briefly in the database. Two secondary gaps: linked rows
still displayed the imported WhatsApp handle instead of the profile's registered name, and a
once-confirmed handle never carried forward to future games.

**Resolution:** Three coordinated changes:
1. `ReplaceParticipantsAsync` merges: `row.PlayerProfileId = participant.PlayerProfileId ?? row.PlayerProfileId`
   — import evidence wins when it resolves, but a null resolution never clears a human link.
2. Roster queries (`ListEligibleRosterAsync`, `GetSessionRosterQueryHandler`) resolve linked
   participants' profiles and display the profile `DisplayName`, falling back to the imported handle.
3. Import reconciliation: keyless handles resolve through `ListLinkedParticipantsByDisplayNamesAsync`
   — a handle previously linked to exactly one profile (any confirmed link) auto-links on future
   imports; ambiguous or "Guest" handles stay unlinked for a human to decide.

**Takeaway:** When a sync job upserts rows that other flows (admin actions, user claims) also
write, every field assignment must decide: source-of-truth overwrite or merge-preserving. Nulling a
field the sync cannot itself derive is destructive — coalesce instead. And once an entity links to
a profile, the profile is the identity: display its name, not the imported alias.
