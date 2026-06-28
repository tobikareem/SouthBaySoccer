# SouthBaySoccer Function App HTTP Collection

Use these `.http` files with Visual Studio, Rider, or the VS Code REST Client extension.

Local prerequisites:

1. Start the Function App from `src/SouthBaySoccer.Functions`.
2. Apply EF migrations to `SouthBaySoccer_Local`.
3. Run `AuthenticationFunctions/authentication.http` -> `Mint a local development admin JWT`, then paste or reference the returned `accessToken` in `@accessToken` / `@adminToken`.

Current local auth caveat:

The normal WhatsApp delivery provider is intentionally unavailable locally, so the auth calls document the public contract but will not produce a usable token until we add a dev-only token path or seed an identity/challenge manually. For now, any authenticated request needs a pasted JWT whose `policy` claims include the needed policy, such as `AuthenticatedPlayer`, `CanManageSessions`, `CanManagePlayers`, `CanCheckInPlayers`, `CanAssignTeams`, and `CanRecordStats`.

Sequential smoke path:

Run `00-local-smoke/local-m9-sequence.http` top to bottom after setting the token and date variables. It creates a season, venue, session, guests, RSVPs, check-ins, a match, stats, and then reads the M9 leaderboard/profile query endpoints.

Notes:

- `local.settings.json` is intentionally ignored by git.
- Payment checkout calls are included for contract coverage, but the local payment gateway is unavailable until Stripe is configured.
- Waiver reads are included, but there is no public API yet to publish a waiver document; seed that table directly or add a dev/admin endpoint later.
- `RecordMatchEvents` currently returns only affected count, not event ids. To approve an event through the API today, query the local DB for the inserted `MatchEvents.Id` and paste it into `@matchEventId`.