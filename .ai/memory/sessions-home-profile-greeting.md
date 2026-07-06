# Sessions Home Profile Greeting

The authenticated Sessions home header should build its greeting from the current local device time
and `IProfileClient.GetCurrentProfileAsync()` (`profiles/me` in API mode). Use the first word of
`PlayerProfileDto.DisplayName` for copy such as `Good morning, Tobi`. If the profile call fails or
returns no display name, fall back to the dashboard-provided greeting so the home screen still loads.
Cache the profile display name in the Sessions page model so repeated tab appearances do not call `profiles/me`; explicit refresh can force a profile reload.

