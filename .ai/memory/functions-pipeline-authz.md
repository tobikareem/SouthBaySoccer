# Functions Pipeline Authorization

SouthBaySoccer Functions startup registers the HTTP pipeline as correlation, exception,
authentication, then authorization. Every HTTP endpoint must declare exactly one access marker:
`[AllowAnonymous]` or `[RequirePolicy("PolicyName")]`. Missing, conflicting, or empty policy
metadata fails closed through `EndpointClassificationException`.
