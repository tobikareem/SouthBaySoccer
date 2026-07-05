# AUTH-8 - Pickup Pal phone sign-in - Tasks

AUTH-8 slice of milestone **M11** (full roadmap: [`../../tasks.md`](../../tasks.md)).

- [x] **M11.1** `Contracts`-based typed API services + `HttpClient` pipeline
  (`CorrelationIdHandler` -> `AuthenticationHandler` -> `ApiExceptionHandler`); secure token storage and single-flight refresh are implemented.
  - Stories: `AUTH-4`, `NFR-Security`.

- [x] **M11.3b** (AUTH-8 slice) Add international phone validation, busy/error/offline states, and the Welcome Back sign-in command. External-return alone must not authenticate.
  - Stories: `AUTH-8` - Projects: MAUI client - Depends on: M11.1, M11.3a.

- [x] **M11.3c** Implement Pickup Pal phone sign-in: call the anonymous Function endpoint, prevent duplicate submission, sync the Pickup Pal user locally, issue/store SouthBaySoccer tokens, and replace the auth route with the Sessions Shell only after success.
  - Stories: `AUTH-8`, `AUTH-3`, `AUTH-4` - Projects: Contracts, Functions, Application, Infrastructure, MAUI client - Depends on: M3.4, M11.1, M11.3b.

  Current: contracts, typed client, secure storage handoff, phone lookup endpoint, Pickup Pal API client, local user/profile sync, token issuance, refresh-token persistence, and focused client/application/infrastructure/functions tests are implemented.

- [x] **M11.3d** (AUTH-8 slice) Add tests for validation blocking the API call, single-submit while busy, success storing tokens and navigating, not-found sign-up prompt without navigation, service failure recovery, Pickup Pal user sync, and endpoint metadata/problem mapping.
  - Stories: `AUTH-8` - Depends on: M11.3c.

**Done when:** all AUTH-8 scenarios have passing tests, phone sign-in authenticates only after a Pickup Pal user match and SouthBaySoccer token issuance, and no sensitive values are logged.
