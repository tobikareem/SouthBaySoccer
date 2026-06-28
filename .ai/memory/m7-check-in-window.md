# M7 check-in window and late override

- Check-in window enforcement belongs in Application, using `IClock.UtcNow` against the session's stored `CheckInOpensAtUtc` and `CheckInClosesAtUtc` timestamps.
- Normal check-ins inside the stored window do not create `AdminOverride` rows, even if a late override reason is accidentally supplied.
- Outside-window check-ins require a trimmed late override reason; the repository writes an `AdminOverride` with `OverrideType.CheckIn`, actor, target player, session, reason, and applied UTC timestamp in the same check-in transaction.