---
name: group-broadcast-notifications
description: Admin broadcasts are group-scoped in-app announcements with a read-only player feed
type: product
created: 2026-07-27
---

SouthBaySoccer notifications use an admin-only, group-scoped broadcast model. The composer runs
*audience → message → how it lands → send*: a single-select row of group cards (name + member
count, not a dropdown), a 500-character editor with the counter on the label line, an announcement
preview, a push toggle that reveals an OS-style push preview, and a bottom-docked CTA carrying the
exact recipient count. Selecting a group must update the preview, push preview, recipient count, and
destination feed together. A "Recently sent" list gives admins read receipts.

Players open a read-only Announcements feed from the Sessions notification bell: plain cards (not
chat bubbles) on a white surface, grouped under Today/Earlier, filtered by an All/Unread segmented
control, with Mark all read clearing the cards, the bell dot, and the unread count together. A card
footer appears only when the announcement carries context (chip + `View session` link).

**Read counts are admin-facing only** — players never see "seen by N of M". It is delivery
telemetry for the sender, not information a player can act on, and it clutters the feed.

Admin preview and player feed row share one `AnnouncementCard` template, so the composer shows
exactly what players receive.

**Backend model (M14).** Unread state is a per-player-per-group high-water mark
(`GroupAnnouncementReadMarker.LastReadAtUtc`), not a receipt row per recipient: sending is one
insert regardless of group size and "mark all read" is one update. Three rules make it correct and
must not be undone:
- The mark is set to the group's **newest `SentAtUtc`**, never to `clock.UtcNow` — a clock reading
  can outrun an announcement that is stamped but not yet committed, which would mark an unseen
  broadcast read forever.
- Unread is floored at the player's `PlayerGroupLink.CreatedAt`, so joining a group does not
  inherit its back catalogue as unread.
- `Announcement.RecipientCount` snapshots **linked members excluding the author**, not
  `GroupChat.WhatsAppMemberCount`; the read-receipt numerator and denominator must count the same
  population or "seen by 12 of 8" becomes possible.

Feed paging uses a composite `(SentAtUtc, Id)` cursor because a timestamp alone cannot order tied
sends and would leave a row unreachable by any page.

The authoritative design is in `documentation/mobile-wireframes.html`; the implementation contract
is `_specs/client-ui.md` §11.1.

Related: [[mobile-wireframes-design-source]], [[client-reusable-ui]]
