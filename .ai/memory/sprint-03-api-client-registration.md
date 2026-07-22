# Sprint 03 API Client Registration

As of the Sprint 03 API-client pass, MAUI API mode registers typed providers for all committed client
interfaces: auth, profile, players, session admin, sessions, roster, stats, leaderboard, and game-day.
Debug API mode must keep these API registrations primary; seed fallback registrations use `TryAdd`
so they only fill genuinely absent interfaces.

Several clients still expose explicit missing-contract or empty read states because the backend lacks
the required screen-shaped projections: roster going/waitlist reads, game-day context/captain/draft
closeout reads, and match-stat self-submit/confirmation projections carrying current-player and
submission ids.
