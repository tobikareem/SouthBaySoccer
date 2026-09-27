---
name: game-day-today-projection
description: Game Day is a server-authoritative Pacific-day projection with IDOR-safe self check-in
type: project
created: 2026-07-22
---

`GET game-day/today` is the sole source of truth for whether the current Pacific calendar date is a
game day. It returns only Published, non-deleted sessions and prefers a same-day session where the
current player has a confirmed local RSVP or linked, non-waitlisted Pickup Pal participant row.
The server computes check-in eligibility and capabilities from `IClock.UtcNow`, stored UTC session
windows, payment/waiver eligibility, and policies; the MAUI client only formats UTC timestamps.

Self check-in uses `POST sessions/{sessionId}/check-ins/me`: the server derives the player profile
from the authenticated identity and never accepts a client-supplied player id. It is allowed at the
inclusive stored open/close boundaries. After close, `CanCheckInPlayers` operators use the existing
admin check-in route with a mandatory reason; the outcome is forced to Late and an AdminOverride is
written. Check-in does not mutate RSVP intent.

If no session starts today, the API returns 204 and the tab shows `No session today`. Canceled and
soft-deleted sessions are excluded by status/query filter. Pickup Pal refresh is isolated from the
read query, limited to five seconds, single-flight, and throttled to once per minute; failures fall
back to persisted sessions.

Related: [[pickuppal-games-import]], [[m7-check-in-window]], [[functions-pipeline-authz]]
