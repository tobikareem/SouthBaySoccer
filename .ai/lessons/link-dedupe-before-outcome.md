---
name: link-dedupe-before-outcome
description: Dedupe keys for single-use links must be set only after a definitive outcome, and must not be cleared by flow resets
type: lesson
created: 2026-09-14
---

## What happened

`OnboardingFlow` recorded a "last dispatched (kind, token)" key before redeeming a bot link, and
`Reset()` cleared it. Two bugs followed: a transient network failure left a still-valid link
permanently ignored on that run (retap did nothing, no message), and the login path's `Reset()`
wiped the key so a duplicate cold-start + warm-intent delivery ran the redemption twice.

## Rule

- A dedupe key represents "this token was consumed". Set it when dispatching, but clear it again
  on transport, timeout, or unexpected failure so the same link is retryable; keep it on a
  definitive server answer (success or a token-failure problem).
- Never clear the dedupe key from a generic `Reset()`; flow state and token consumption are
  different lifetimes.
- Do the "already authenticated / already handled" check inside the same lock as the dispatch so a
  queued duplicate observes the post-dispatch state.
- Distinguish "token failure" from "endpoint missing" by problem `type`, not by HTTP status, when
  the API pipeline throws before the client sees the response.

Regression tests: `OnboardingFlowTests.HandleAppLink_SameLoginLinkDeliveredTwice_CompletesOnce`
and `HandleAppLink_TransportFailure_AlertsAndLeavesLinkRetryable`.
