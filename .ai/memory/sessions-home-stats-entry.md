# Sessions home stats entry

The first authenticated Sessions page always shows the `Submit your latest stats` card.

- When `SessionsDashboardDto.StatsPrompt` contains a non-empty match id, the card opens the
  `matchstats` detail route for that match.
- When API mode has no latest-match projection, the card falls back to the AppShell-owned `//stats`
  root tab instead of disappearing.
- A successful dashboard load remains in Content state even when no sessions are returned, so the
  always-available stats entry point is not hidden by `StateView`.
- The gesture belongs to the outer `BrandCard`, making its full padded surface actionable.
