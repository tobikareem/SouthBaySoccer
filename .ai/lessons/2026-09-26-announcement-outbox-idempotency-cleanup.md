# Discard failed announcement writes before idempotency cleanup

Announcement posting and IdempotentRequestExecutor use the same scoped EF context. If adding or
saving an announcement/outbox pair fails, AbandonAsync subsequently saves that context while
clearing the request key. Pending tracked announcement changes must be discarded before rethrowing,
otherwise cleanup can inadvertently commit the failed operation. Likewise, discard tracked changes
when the immediate external-send settlement fails, before idempotency completion saves the response.

Rule: persist local announcement + leased delivery intent together; contact Pickup Pal only after
commit; discard failed tracked changes on both save boundaries. Do not log provider exceptions or
claim exactly-once external delivery without a provider deduplication contract.
