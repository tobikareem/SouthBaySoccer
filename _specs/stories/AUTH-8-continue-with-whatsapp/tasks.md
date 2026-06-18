# AUTH-8 — Continue with WhatsApp · Tasks

AUTH-8 slice of milestone **M11** (full roadmap: [`../../tasks.md`](../../tasks.md)).

- [~] **M11.1** `Contracts`-based typed API services + `HttpClient` pipeline
  (`CorrelationIdHandler`→`AuthenticationHandler`→`ApiExceptionHandler`); secure token storage.
  — Stories: `AUTH-4`, `NFR-Security`. *(Prerequisite for the challenge client.)*

- [x] **M11.3b** (AUTH-8 slice) Add international phone validation, busy/error/offline states, and the
  `RequestWhatsAppChallengeCommand`. External-return alone must not authenticate.
  — Stories: `AUTH-8` · Projects: MAUI client · Depends on: M11.1, M11.3a.

- [~] **M11.3c** Implement the WhatsApp one-time challenge client flow and approved deep-link callback:
  request challenge, prevent duplicate submission, verify/exchange the callback, store tokens
  securely, and replace the auth route with the Sessions Shell only after success.
  — Stories: `AUTH-8`, `AUTH-3`, `AUTH-4` · Projects: Contracts, Functions, Application, MAUI client
  · Depends on: M3.4, M11.1, M11.3b.

  Current: contracts, typed client, secure storage, startup refresh, platform callback handling,
  exactly-once client navigation, and client tests are implemented. Function endpoints, Pickup Pal
  verification, Identity token issuance, and refresh-token persistence remain blocked on M3.

- [~] **M11.3d** (AUTH-8 slice) `Client.Tests`: validation blocks the client call; single-submit while
  busy; failure preserves the number and restores the command; deep-link completion stores tokens and
  navigates once. `Functions.Tests`: verify/exchange issues rotating tokens, rejects forged challenges.
  — Stories: `AUTH-8` · Depends on: M11.3c.

  Current: client scenarios pass. Functions tests remain pending with the M3 backend.

**Done when:** all AUTH-8 scenarios have passing tests, the challenge flow authenticates only on a
verified exchange, and no sensitive values are logged.
