# M3 WhatsApp Session Authentication

Sprint 02 M3 is WhatsApp-number session authentication, not email/password auth. Email/password
registration, confirm-email, sign-in, and password reset are out of scope. The backend authority is
a verified WhatsApp challenge exchanged for server-issued JWT access tokens and rotating refresh
tokens.

M3.4 keeps the public challenge-request response non-secret: it returns only challenge metadata.
Raw one-time challenge tokens must stay inside the WhatsApp delivery/provider path, and persistence
stores only hashes for challenge tokens, phone numbers, and callback URIs.
