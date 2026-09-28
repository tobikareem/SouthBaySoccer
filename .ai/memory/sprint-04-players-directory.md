# Sprint 04: Players Directory

Sprint 04 tracks making the MAUI Players tab data-driven from backend player data. The source of
truth is the populated identity/profile data in `AspNetUsers` and `PlayerProfiles`; the MAUI screen
must keep using the wireframe baseline and load real player rows through an API-backed
`IPlayersClient`.

Sprint 04's automated implementation and API smoke are complete as of July 21, 2026, but the sprint
remains open pending the native MAUI API-mode smoke. The public directory contract uses
`PlayerProfiles.Id` as its stable navigation key and intentionally excludes internal identity ids
and contact data. Local authenticated API smoke returned 127 database-backed rows and loaded profile
detail successfully; repository, Function response, API error/cancellation, refresh, and page-model
cancellation tests cover the slice. Windows and Android builds passed with zero warnings.

The global directory now sorts all active profiles across all groups by the equal-weight career
sum of goals + assists + unrounded average rating + MVP awards, descending, with invariant
case-insensitive name then profile-id ties. Zero-stat players remain listed. Infrastructure's
`PlayerStatAggregateReader` is shared with leaderboard/profile queries; directory matches are
eligible played appearances, not all participant rows. No per-player stats calls or stored totals;
the existing 60-second server directory cache and DTOs are unchanged.
