# Functions ProblemDetails

SouthBaySoccer Functions map HTTP exceptions to RFC 7807 `ProblemDetails` with statuses
400, 401, 403, 404, 409, 410, 412, 429, 500, and 503. Responses include `x-correlation-id` and a
`correlationId` extension, but unexpected errors use a generic detail and logs avoid exception
messages to prevent leaking secrets, payment data, or personal data.

Onboarding token outcomes use an absolute type the MAUI client matches on (never status alone):
`https://southbaysoccer/problems/onboarding-token-{expired|invalid|already-registered|mismatch}`
(`ProblemDetailsMapper.OnboardingTokenProblemTypePrefix`). See [[m13-onboarding-backend]].
