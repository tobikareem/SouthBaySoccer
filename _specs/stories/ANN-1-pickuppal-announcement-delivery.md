# ANN-1 — Send group announcements through Pickup Pal

Approved group admins (and approved-member owners) post an announcement as before. The backend
also sends its trimmed message to the same group's stored external WhatsApp chat ID using
`POST /api/groupchat/send` with JSON `{ "chatId": "...@g.us", "message": "..." }`.
The MAUI client never chooses an external destination or holds provider credentials.

Save the announcement and a delivery outbox row in one SaveChanges call before contacting Pickup Pal.
The initial row has a five-minute processing lease so the existing timer cannot race the immediate
send. Attempt delivery immediately after commit. Success marks the row processed; temporary network,
timeout, 408, 429, or 5xx failures leave a scheduled retry; permanent rejection dead-letters it.
An immediate send/settlement failure must not turn a saved announcement into a failed local post.
The existing outbox timer retries the stored announcement, not the announcement creation command.
No new table, migration, Firebase integration, or push notification is required.

Outbox payload contains only the announcement ID. Never log the message, provider response body,
external chat ID, or credentials. Use existing PickupPal base URL and optional configured API key.
A missing/deleted announcement or missing group destination is a permanent delivery failure.

The provider contract does not document idempotency. Delivery is at least once: an ambiguous timeout
or crash after the external send but before local settlement can produce a duplicate group message.
Do not claim exactly-once delivery. Local request idempotency remains unchanged.

Acceptance: authorized post saves announcement + leased intent atomically then sends exact group/body;
unauthorized/invalid posts and failed first commits never send; upstream failure preserves the local
announcement and retry intent; replayed outbox rows never insert another announcement; HTTP request,
error mapping, cancellation, and DI registration have focused tests. Use fake HTTP only for verification.
