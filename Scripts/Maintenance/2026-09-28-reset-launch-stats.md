# Launch player-stat reset

Applied September 28, 2026 to the production SouthBaySoccer database at the user's request.
The matching SQL file is a one-time audited maintenance operation, not an application migration.

## Scope and result

Soft-deleted 316 pre-launch facts: 13 match events, 151 rating votes, 10 likes, 3 awards,
122 participation rows, and 17 team results. All six tables had zero active rows after commit.
All affected match dates were in July/August 2026. Player profiles (246), memberships (25),
sessions (64), and RSVPs (162) had identical counts before and after. Matches, team assignments,
check-ins, provider snapshots, payment data, and existing correction audits were not changed.

Goals, assists, ratings, MVPs, appearances, minutes, likes, wins/losses, and recent form are derived
from these facts; there is no separate totals table to clear. Pickup Pal import does not recreate
these stats. An explicit future admin edit can create new facts; this reset is not a permanent
ban on editing old matches.

## Execution and repeat protection

Run using a secure SQL connection, with `sqlcmd -v ApplyReset=0` for the transactional preview or
`-v ApplyReset=1` to commit. A fixed cutoff and exact per-table counts prevent a changed dataset
from being reset accidentally. The operation writes an immutable `LaunchStatsReset` audit entry
with actor `maintenance:launch-stats:2026-09-28` in the same transaction. Reruns detect this marker
and make no changes. Do not change the marker/counts to reuse this script on later live data.

Rows retain their original fact values. A recovery, if requested, must target only soft-deleted
rows bearing this operation's `UpdatedBy` marker and verify filtered unique-key collisions against
new records before restoring. Never restore every deleted row or remove the audit history.

## Client visibility

Server leaderboard and directory caches expire after 60 seconds. The current-profile client cache
can last five minutes. Refresh the Players tab after expiry; reopen the app if it still displays
old profile totals. Empty ratings may display a dash instead of zero.

Global Players ranking is a separate backend code change: goals + assists + average rating + MVP
awards, descending, with name and profile ID as stable ties. It does not need a new iOS binary.
