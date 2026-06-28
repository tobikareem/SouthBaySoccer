# M8 teams, matches, and stats

- Stats stay at the `Match` grain: teams, assignments, results, participation, raw events, ratings, likes, MVP awards, and corrections all anchor to `Match`.
- `PlayerMatchStats` is participation-only. Goals and assists are derived from `MatchEvents`; assists are nullable `AssistPlayerProfileId` on `Goal` events, not separate stat rows.
- MVP is an explicit `MatchAward` row. Rating votes may inform UI/admin choice, but they are not the persistence authority for MVP.
- Profile stat reassignment updates match-grain rows from guest profile to claimed profile and soft-deletes duplicates to preserve unique constraints. Add a dedicated audit record before treating this as production-complete.
- Captain approval queue and conflict-to-review are still follow-up details on top of the raw-recording/lock/correction foundation.

