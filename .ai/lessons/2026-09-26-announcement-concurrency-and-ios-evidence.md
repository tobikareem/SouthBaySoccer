# Announcement scope, races, and iOS evidence

User correction: enabling previously disabled announcement entry points is not evidence that the
historical iOS watchdog failure was fixed. Keep regression evidence distinct from root-cause proof.
The historical disable commit `300401f` records 0x8BADF00D; earlier `0d2fb58` predates that workaround.
No later root-cause finding or app crash report was found during this investigation.

On 2026-09-26, the updated Debug iossimulator-arm64 app built with zero warnings/errors and ran on
an iPhone 17 Pro simulator with iOS 26.3, in Seed mode. Both screens opened and reopened, the feed
handled All/Unread, mark-read, empty unread state, picker open/close and pull-to-refresh. Composer
handled a 500-character multiline body and live preview, then back navigation. The app stayed alive
and responsive beyond two minutes. No real announcement was sent. This did not reproduce the freeze;
it does NOT establish its cause or exclude physical-device, release-build, network, large-feed or
multi-group-only failures (Seed had one approved group). Obtain a device crash report/thread sample
if it recurs instead of speculatively changing layouts.

Concurrency rules: capture group AND load generation for feed responses/errors; keep busy state
until all outstanding operations finish. Do not let a same-group refresh race a read-marker write.
History must filter the authorized selected group before limiting. A lost draft audience requires
explicit selection and must never silently fall back to another group. Bell count and destination
must derive from the same unread scope. Complete idempotency after a committed operation with a
bounded independent token, even when the incoming request has been cancelled.
