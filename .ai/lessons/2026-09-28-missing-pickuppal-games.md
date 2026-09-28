# Missing Pickup Pal games: check season coverage and feed cutoff separately

On September 28, production had four active Pickup Pal games but only Summer 2026 ending at
2026-09-30 00:00 UTC. All four games started later; even September 29 at 8:30 PM Pacific was
September 30 03:30 UTC. Import returned four warnings and skipped them. Compare provider UTC start
instants with database season bounds before assuming an import HTTP error. A season rollover needs
an explicit product decision; do not silently move matches between statistical seasons.

Separately, Sessions passed clock.UtcNow as its lower bound, hiding successfully imported games
at kickoff. It now uses Pacific midnight, consistent with Game Day, preserving today's feed-backed
detail routes. Test UTC date boundaries and both daylight-saving transition days.

With user approval, Fall 2026 was added in production on September 28, covering
2026-09-30 00:00:00.0000001 UTC through 2027-01-01 07:59:59.9999999 UTC. This follows
the existing season without overlap and includes December 31 Pacific. The insert was guarded
against duplicates and overlaps and verified by readback. No sessions were imported at that
immediate readback. The active-season cache lasts five minutes, independently of the import's
one-minute throttle; allow cache expiry before using app refresh to verify import.
