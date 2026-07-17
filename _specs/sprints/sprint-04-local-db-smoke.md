# Sprint 04 Local DB Smoke - Players Tab

Use this runbook to verify the Players tab against a local Function App and a database that already
has populated `AspNetUsers` and `PlayerProfiles` rows. Do not commit connection strings, bearer
tokens, phone numbers, or copied response bodies that contain private data.

## Prerequisites

- `src/SouthBaySoccer.Functions/local.settings.json` exists locally and points at the intended local
  database.
- EF migrations have been applied to that database.
- The database has active, non-deleted rows in `PlayerProfiles`, with linked rows in `AspNetUsers`
  where the players are registered app users.
- The local Function App is running at `http://localhost:7071/api`.
- A local development JWT is available through `POST /api/dev/local-admin-session` or another
  dev-only auth path.

## Known local host blocker

On July 7, 2026, an automated smoke attempt could not start the local Function host because Function
Core Tools failed before serving requests with:

```text
An item with the same key has already been added. Key: AZURE_FUNCTIONS_ENVIRONMENT
```

Before rerunning the live smoke, remove the duplicate `AZURE_FUNCTIONS_ENVIRONMENT` source from the
local process environment, user secrets, or `local.settings.json`. Do not commit the local settings
file or paste its values into this runbook.

This blocker is local to Azure Functions Core Tools startup. It means the local Function host saw
the same `AZURE_FUNCTIONS_ENVIRONMENT` key from more than one configuration source and crashed before
serving HTTP requests. It does not affect an iOS app build that points at a deployed API.

## iOS deployed API smoke

For an iOS debug or release build that should talk to the deployed test API, configure the MAUI app
without committing secrets or local-only files:

```json
{
  "ClientDataSource": "Api",
  "PrdApiBaseUrl": "https://carepath-api-hvhxgvhxejc0fmg3.westus2-01.azurewebsites.net"
}
```

`ClientDataSource=Api` selects the typed API clients instead of seed providers.
`PrdApiBaseUrl` is read by the MAUI client and normalized to include `/api/` when needed, so the
above host value resolves to the Function App API root. Release builds default to API mode, but keep
the explicit setting for test packages so the data source is unambiguous.

## API smoke

1. Start the Function App from `src/SouthBaySoccer.Functions`.
2. Open `http/00-local-smoke/local-m9-sequence.http` or
   `http/ProfileFunctions/profiles.http`.
3. Run the local admin session request and keep the returned bearer token in the REST client
   variable only.
4. Run:
   - `GET {{baseUrl}}/players/directory`
   - `GET {{baseUrl}}/profiles/{{playerProfileId}}`
5. Verify:
   - The directory response has `totalPlayers > 0`.
   - At least one returned row has a `player.id` matching a `PlayerProfiles.Id` value from the local
     database.
   - Linked registered players include `identityId`; guest profiles can have `identityId: null`.
   - No response includes phone numbers, email addresses, token values, payment identifiers, or
     emergency contact data.
   - `GET /profiles/{playerProfileId}` returns display name, position, career stats, recent form,
     and role for a row selected from the directory.

## MAUI smoke

1. Run the Function App locally.
2. Configure the MAUI app for API mode without committing the setting:
   - Windows: provide `ClientDataSource=Api` through the local launch environment/configuration.
   - Android emulator: API base URL should resolve to `http://10.0.2.2:7071/api/`.
3. Sign in with a local development token/session.
4. Open the Players tab.
5. Verify:
   - The count badge matches the directory response count.
   - Player rows match real local database profiles rather than seed names.
   - Search still filters by name, position, and row subtitle.
   - Tapping a player opens the profile tab with that player's display name and career stats.

## Evidence to record

Record only non-sensitive evidence in the sprint board: command names, pass/fail status, counts, and
sanitized notes. Do not paste bearer tokens, phone numbers, email addresses, or raw response payloads.
