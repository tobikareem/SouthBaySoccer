# Release mobile API default

Standalone MAUI Release builds default to API mode and use
`https://southbaysoccerfunc-cndha8gtc4bxdtfe.westus2-01.azurewebsites.net/api/` when no explicit
`PrdApiBaseUrl` or `ApiBaseUrl` configuration is available. Debug Android continues to default to
the emulator loopback URL. Keep the Release fallback aligned with `Scripts/Run-MobileRelease.ps1`.
