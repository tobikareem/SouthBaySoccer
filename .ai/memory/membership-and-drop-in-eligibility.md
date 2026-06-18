---
name: membership-and-drop-in-eligibility
description: Eligibility supports both monthly membership and session-specific guest payments
type: convention
created: 2026-06-18
---

SouthBaySoccer supports both monthly membership eligibility and one-time guest/drop-in eligibility
for a specific session. These are separate concepts in the payment ledger and eligibility model.
Neither may be manually marked paid; verified Stripe webhooks remain authoritative.

**Why:** Members need recurring dues while guests need limited eligibility for one session.

**How to apply:** Model membership state independently from session-specific payment eligibility.
RSVP checks may accept either valid monthly membership or a verified drop-in payment for that
session, plus the required waiver.

Related: [[stripe-webhook-source-of-truth]]
