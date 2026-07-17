# Pickup Pal Phone Sign-In

Pickup Pal is the source of truth for SouthBaySoccer user identity/profile data. Sign-in now uses
SouthBaySoccer Functions to look up the submitted phone number through Pickup Pal
`GET /api/users/phone/{digits}` and, when found, syncs a local `ApplicationIdentityUser` plus
`PlayerProfile` before issuing SouthBaySoccer JWT access and rotating refresh tokens.

This is not currently WhatsApp authentication. Users sign in by entering their phone number in the
SouthBaySoccer app; the backend confirms the number exists in Pickup Pal. WhatsApp challenge links,
callbacks, and one-time-token verification are deferred.

Persist Pickup Pal email on `ApplicationIdentityUser.Email` for future notifications. Do not store
raw phone numbers locally; store hashes and masked phone display only. SouthBaySoccer roles remain
local (`PlayerProfile.Role`) and are not overwritten by Pickup Pal profile sync.
Pickup Pal preferred soccer positions come from the full user profile endpoint `GET /api/users/{id}` at
`userInfo.sportsInfo[]` where `sport == "SOCCER"` and `isActive == true`; sync them into
`PlayerProfile.PreferredPosition` as a comma-separated string such as `st, rw, cm`.
Configured AdminPhoneNumbers is a root Functions setting containing comma-separated phone numbers. Normalize each entry to +{digits}, compare raw Pickup Pal sign-in phones transiently during sync, and compare hashes on profiles/me; matching users are promoted to local PlayerRole.GameAdmin unless already Owner, Admin, or GameAdmin.

**Exception to "no personal data in URLs":** `PickupPalUserClient.FindByPhoneAsync`
(`src/SouthBaySoccer.Infrastructure/Authentication/PickupPalUserClient.cs`) calls
`GET api/users/phone/{digits}`, putting the raw phone number in the request path. This is forced by
Pickup Pal's external API contract and must not be changed. Because the phone number lives in the URL
rather than a request body, request URIs on this call path must never be logged — no `ILogger` call,
`DelegatingHandler`, or telemetry/tracing config may record the outbound request URI for this client.
As of this writing no code in Infrastructure or Functions logs it explicitly (`AddHttpClient<IPickupPalUserClient, PickupPalUserClient>()`
has no attached message handlers and Infrastructure has zero `ILogger` calls), but note that default
ASP.NET/Functions HTTP client telemetry (e.g. OpenTelemetry's `AddHttpClientInstrumentation`, or the
classic Application Insights dependency tracking module) records full outbound URLs by default — if
either is ever wired up for this app, this HttpClient needs an explicit URL-redaction rule before that
happens.

