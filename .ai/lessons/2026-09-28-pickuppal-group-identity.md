# Group identity must be tested through the games client

Approved Bay Area Soccer members could not RSVP because newly imported sessions had null GroupChatId.
The live feed supplied top-level groupId and group.id, while the mapper read only group.groupId.
The membership gate correctly failed closed. Accept the canonical top-level groupId and both nested
variants, preserving JsonIgnore on GroupExternalId. Test active-list and single-game mapping, including
missing keys, trimming, precedence and sanitized serialization. Do not infer identity from group names
or weaken membership authorization to hide a mapping failure.

On September 28 the four active sessions were repaired in production by exact provider game ID and
catalogue ExternalId, changing only null imported-session group links with audit stamps. No memberships
were approved or RSVP submissions made. The backend mapper fix is still needed for future imports.
