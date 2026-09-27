---
name: profile-merge-feedback-final-keys
description: Reconcile both peer-feedback identities together so profile merges cannot create self-feedback or hidden key collisions
area: data
created: 2026-07-30
---

**Problem:** Reassigning rating/like giver and receiver foreign keys in separate passes can turn
source-to-target feedback into a prohibited self-vote/self-like. It can also miss uniqueness
collisions that exist only after both sides use the canonical profile.

**Rule:** Load every active feedback row involving either source or target, calculate the complete
post-merge key, then reconcile by that final key. Soft-delete rows whose final giver and receiver
are the same. For collisions, retain the already-canonical row and soft-delete the duplicate before
updating any survivor. Never rewrite a self-feedback tombstone to a self-referential key because SQL
check constraints apply to deleted rows too.

SQL integration tests must cover source-to-target feedback, target-to-source feedback, and
giver-side and receiver-side final-key collisions for both ratings and likes.
