namespace SouthBaySoccer.Functions.Onboarding;

/// <summary>
/// Normalizes client-supplied timestamps to <see cref="DateTimeKind.Utc"/> at the transport boundary,
/// so validation and persistence never see a <see cref="DateTimeKind.Unspecified"/> or local value.
/// </summary>
public static class UtcDateTime
{
    /// <summary>
    /// Returns the value as UTC: local values are converted, unspecified values are assumed to be
    /// UTC already (the contract documents the field as UTC), UTC values pass through.
    /// </summary>
    /// <param name="value">The client-supplied timestamp.</param>
    public static DateTime Normalize(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
