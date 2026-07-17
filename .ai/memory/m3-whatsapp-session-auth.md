# M3 Phone Number Session Authentication

Sprint 02 M3 is phone-number session authentication, not email/password auth and not WhatsApp
challenge/link authentication. Email/password registration, confirm-email, sign-in, and password
reset are out of scope.

The current backend authority is a direct phone lookup through Pickup Pal: SouthBaySoccer receives
the submitted phone number, calls Pickup Pal to confirm that the phone exists in its user database,
syncs local identity/profile records, then issues SouthBaySoccer JWT access tokens and rotating
refresh tokens.

WhatsApp challenge delivery, one-time links, and callback verification remain deferred. Existing
`WhatsApp*` names in code are legacy naming from the earlier design and should not be treated as the
current product authentication model.
