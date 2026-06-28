---
name: m9-leaderboards-queries
description: M9 leaderboard and profile stats queries derive from approved raw match facts
type: convention
created: 2026-06-27
---

M9 leaderboard/profile stat queries are derived-on-read from raw M8 facts. Aggregate through
`Match -> Session -> Season`; do not add mutable totals tables or profile stat counters. Count only
approved leaderboard-visible facts, exclude `NeedsReview` matches, exclude own goals from scorer
credit, count assists from `AssistPlayerProfileId` on approved goal events, and derive recent form
from `TeamAssignment + MatchResult`. See `_specs/m9-leaderboards-queries.md`.
