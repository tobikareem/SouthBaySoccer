---
name: stripe-webhook-source-of-truth
description: Payment status is driven by verified Stripe webhooks, never the client
type: convention
created: 2026-06-17
---

Membership/payment status must be derived from verified, idempotent Stripe webhook events (`invoice.paid`, `invoice.payment_failed`, `customer.subscription.deleted`, etc.). The client/app never holds Stripe secret keys and an admin never manually toggles "paid".

**Why:** Trusting the client or manual flags leads to incorrect billing state and disputes.
**How to apply:** Verify the webhook signature, store processed event IDs for idempotency, and update state only from events. Keep Stripe SDK calls server-side behind an `IPaymentService` abstraction.
