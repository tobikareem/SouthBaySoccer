# Functions ProblemDetails

SouthBaySoccer Functions map HTTP exceptions to RFC 7807 `ProblemDetails` with statuses
400, 401, 403, 404, 409, 429, and 500. Responses include `x-correlation-id` and a
`correlationId` extension, but unexpected errors use a generic detail and logs avoid exception
messages to prevent leaking secrets, payment data, or personal data.
