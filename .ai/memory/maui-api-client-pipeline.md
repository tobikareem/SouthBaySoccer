# MAUI API Client Pipeline

MAUI API mode uses `HttpClientFactory` with the shared client pipeline:
`CorrelationIdHandler` -> `AuthenticationHandler` -> `ApiExceptionHandler`. Protected typed clients
should be registered through this pipeline so access tokens are attached automatically and expired
tokens are refreshed once through `IAuthenticationSessionRefresher`. Authentication endpoints use the
anonymous client to avoid recursive refresh calls.
