# September 2026 launch stats reset and global Players ranking

On 2026-09-28, the user requested a clean slate for every player's stats before going live.
Production reset soft-deleted 316 pre-launch facts in MatchEvents, PlayerRatingVotes, PlayerLikes,
MatchAwards, PlayerMatchStats, and MatchResults. All six had zero active rows after commit;
accounts, groups, sessions, RSVPs, check-ins and teams were preserved. No Pickup Pal writes.

See Scripts/Maintenance/2026-09-28-reset-launch-stats.md and the matching guarded SQL script.
Audit actor: maintenance:launch-stats:2026-09-28. Do not repeat the reset on later live stats.

Players directory ranking is global career score: goals + assists + average rating + MVP awards,
descending, then invariant case-insensitive display name and profile ID. Include all active
profiles even with no stats. Reuse the leaderboard aggregation's approved/played match rules;
never introduce mutable stat counters. Metric-specific leaderboards keep their existing ordering.
