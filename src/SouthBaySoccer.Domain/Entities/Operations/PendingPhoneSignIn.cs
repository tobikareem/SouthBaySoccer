using System;
using SouthBaySoccer.Domain.Entities.Common;

namespace SouthBaySoccer.Domain.Entities.Operations;

/// <summary>
/// Represents a phone sign-in that is waiting for the player to prove possession through the
/// Pickup Pal <c>!!login</c> WhatsApp token. A login token may only complete the sign-in it was
/// started for. Immutable operational record: never soft-deleted, never removed by ordinary EF deletes.
/// </summary>
public class PendingPhoneSignIn : BaseEntity
{
    /// <summary>Gets or sets the Pickup Pal user id matched when the sign-in started.</summary>
    public string PickupPalUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA-256 hash of the E.164 phone number that started the sign-in.</summary>
    public string PhoneNumberHash { get; set; } = string.Empty;

    /// <summary>Gets or sets when the pending sign-in can no longer be completed (UTC).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets when the login token completed this sign-in (UTC).</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Gets or sets whether the player asked to remember the device at completion.</summary>
    public bool? RememberDevice { get; set; }

    /// <summary>
    /// Gets or sets the SQL row version. Consumption is a read-then-write; the version turns two
    /// concurrent completions of the same login token into one winner and one concurrency failure.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Determines whether the pending sign-in can still be completed at the given instant.</summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <returns><see langword="true"/> when unconsumed and unexpired.</returns>
    public bool IsActiveAt(DateTime nowUtc) => ConsumedAtUtc is null && ExpiresAtUtc > nowUtc;
}
