# Game Day draft revision and adaptive polling

- `Match.DraftRevision` is the server-owned concurrency token for team-draft state. It advances
  exactly once for each material captain topology, pick/bulk correction, auto-balance, lock, or
  unlock mutation. Rejected and no-op mutations do not advance it.
- Keep `DraftRevision` independent from `AutoBalanceVersion`; the latter only selects the next
  deterministic auto-balance deal.
- Draft/team GET endpoints use a strong composite draft/roster ETag, plus
  `Cache-Control: private, no-cache`, and return an empty `304 Not Modified` only after caller
  authentication and authorization. Updated clients send the same revision in `If-Match` and the
  server rechecks it inside the serializable transaction. During mobile rollout a missing header is
  accepted for compatibility, but malformed supplied values fail validation and stale revisions use
  typed `412 draft-revision-conflict` rather than generic `409`.
- The MAUI draft page polls only while visible and editable (normally every 2 seconds); the
  spectator teams page uses 5 seconds. Both use jitter, 5/10/30-second failure backoff, one
  non-overlapping page-lifetime loop and immediate refresh after local mutations. The composite GET
  validator prevents roster changes from remaining hidden behind an unchanged mutation revision.
- A background result is applied only when its revision is newer and no local edit or mutation
  became active while the request was in flight. Page disappearance owns cancellation, including
  cancellation during the initial load, so page recreation cannot leak polling timers. Window
  lifecycle events cancel the active polling epoch while backgrounded and resume with an immediate
  refresh.
