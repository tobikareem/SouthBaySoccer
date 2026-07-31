# Player Game Day recent history

- A current Game Day must not replace a player's visibility into prior games.
- Non-admin Game Day uses a `Today | Recent games` switch when both a live context and history exist.
- Recent history contains at most the three newest games where the player was actually checked in;
  Going-only no-shows, waitlisted players, and group-membership-only games are excluded.
- The no-game-today scalar fallback may still use RSVP/group relevance, preserving its broader
  existing behavior.
- Recent summaries reuse the last-game team sheets and eligible follow-up/rating actions.
- Batch venue and match/stat facts across the selected sessions; do not parallelize EF repository
  calls that share the scoped DbContext.
