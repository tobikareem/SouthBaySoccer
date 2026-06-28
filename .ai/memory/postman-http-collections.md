# SouthBaySoccer Postman HTTP Collections

The Postman `SouthBaySoccer` workspace is generated from the repo `http/` folder. Each immediate
subfolder maps to one Postman collection; `00-local-smoke/local-m9-sequence.http` maps to
`LocalM9Sequence`.

Requests should preserve the `.http` request title/comments as Postman request descriptions.
Each request should include a post-request `test` script. `localAdminSession` captures
`accessToken`, `adminToken`, `identityUserId`, `playerProfileId`, and `displayName` into the active
Postman environment. Other requests with `.http` response capture lines should set the captured
environment variables, accepting either camelCase or PascalCase JSON response fields.
