---
name: membership-access-through-every-path
description: Group access must survive imports, promotion, API mapping, and hidden client actions
area: authorization
created: 2026-09-19
---

**Context:** The user reiterated that nonmembers must not RSVP or see RSVP buttons after group
membership was implemented. A previous review proposed restoring the combined toggle for
withdrawal; that would obscure the distinction between joining and canceling.

**Problem:** Imported groups missing from the local catalogue yielded null GroupChatId and open
access. Promotion checked only payments, client defaults could mask missing membership fields,
and hiding a control alone did not guard its command.

**Resolution:** Discover groups during import/catalogue reads; deny unresolved imported-group
access; check membership on every submitted intent and promotion; require explicit approval for
grouped client responses. Hide RSVP/join-waitlist controls and guard commands. Keep a separate
cancel-only action for held spots, independent of capacity.

**Takeaway:** Trace admission end to end through import, projection, API mapping, commands, and
automatic promotion. A cancellation exception must never restore a joining control. Test real
wire-format responses and Seed mode as well as mocked page models.

Related: [[2026-09-19-api-client-must-map-every-access-field]], [[m16-group-membership]]
