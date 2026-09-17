namespace SouthBaySoccer.Functions.Outbox;

/// <summary>
/// Options for the timer-driven outbox processor. Bound from the <c>Outbox</c> configuration
/// section. The cadence itself (<see cref="Schedule"/>) is a compile-time constant on the timer
/// trigger: a <c>%setting%</c> that is missing at deploy time breaks host indexing, whereas a
/// disabled flag is harmless.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>NCRONTAB schedule of <see cref="OutboxFunctions.ProcessOutbox"/>: every 5 minutes.</summary>
    public const string Schedule = "0 */5 * * * *";

    /// <summary>Gets or sets whether the processor runs at all. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of rows one run claims.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Gets or sets how long a claim holds a row before another run may reclaim it.</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the attempt count at which a retryable failure dead-letters the row.</summary>
    public int MaxAttempts { get; set; } = 6;
}
