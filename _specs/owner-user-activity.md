# Owner user activity

## Intended behavior

Profile's owner administration area links to **User activity**. Only the owner (IsSuperAdmin)
can read the API. Group admins, captains, players, and anonymous callers cannot access the data.
Show successful SignUp and SignIn records, newest first: name, local time, first/last recorded
activity, recorded sign-in count, and current group names/membership states. Existing Pickup Pal
users who sign in are SignIn; completing in-app registration is SignUp. Do not infer downloads.

Tracking begins with this release. Never invent/backfill historical events from token rows. Label
first/last/count fields as recorded activity, not lifetime app usage. A successful signup issues a
session but is counted as SignUp, not as an additional SignIn. Token refresh, failed authentication,
onboarding validation, imports, and ordinary app opening do not create activity events.

## Persistence and API

Add one UserActivity entity/table with Guid key, BaseEntity audits/soft deletion, identity/profile
references, type, UTC occurrence time, and a unique session-family reference. Do not duplicate
names, phone numbers, emails, credentials, token values/hashes, IPs, or device identifiers.
Write the event atomically with issuance of the initial refresh token; use IClock.UtcNow. Retried
SaveChanges must not produce duplicate events. Do not attach tracking to token rotation.

GET /api/admin/user-activity?page=1&pageSize=25 returns UserActivityPageDto. Validate page>=1,
page<=10000, pageSize1..100 using FluentValidation in Application. Fetch pageSize+1 to determine
HasMore; stable descending OccurredAtUtc then Id. Join active profiles only, excluding deleted
accounts. Use indexed pagination and grouped reads limited to the page's player IDs; no per-row
queries. TrackingStartedAtUtc is the earliest recorded event timestamp (null if none).
First/last activity and SignInCount summarize retained recorded history, not just the current page.
Membership status is current, not a snapshot from event time. No public caching of this endpoint.

Contracts live in SouthBaySoccer.Contracts.UserActivity. ActivityType values: SignUp, SignIn.
Layering stays Domain <- Application <- Infrastructure; Functions is transport only.
Migration is additive and deployed before updated Functions. Existing clients continue working.

## Client and design

Use existing BrandHeader, BrandCard, StateView, styles and spacing. Add a matching user-activity
screen to the wireframe and client-ui spec; reuse existing admin navigation and owner card layout.
Page offers refresh/retry and Load more; preserve one pagination sequence without duplicate requests.
Refreshing resets pagination. Convert timestamps to local time at the client boundary. Show readable
empty/offline/error states and an access-denied message; never seed fake activity in API mode.
Owner navigation only; API remains the security boundary. The new page requires a mobile release.
Do not add notifications or change existing App Store version as part of this implementation.

## Verification

Tests: initial sign-in creates exactly one event; signup creates SignUp; refresh creates none;
failed writes leave no token/event; timestamps use clock; authentication paths propagate kind;
unauthorized/non-owner rejected; pagination validation/order; deleted profiles excluded; group state
and counts mapped; client route/query, refresh/paging failure/retry, access denied, owner navigation.
Review code and XAML. Build backend and iOS target; run portable tests and SQL tests where supported.

## Rollout and limits

1. Apply the additive migration through the controlled Azure release pipeline.
2. Deploy Functions to start collecting activity immediately for existing app versions.
3. Ship the owner screen in a subsequent iOS build; no mobile update is needed for collection itself.

An event means the authenticated session was committed successfully, not proof that the device
received the response or that a download occurred. Separate explicit sign-ins issue separate sessions;
network retries that issue new sessions can therefore appear as multiple sign-ins. Database-save
retries reuse the same family/event identifiers. No passwords, tokens, or contact details enter logs.
Do not reset existing auth records or replay signups to populate this feature.
