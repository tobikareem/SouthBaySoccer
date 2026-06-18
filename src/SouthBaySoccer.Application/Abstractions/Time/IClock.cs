namespace SouthBaySoccer.Application.Abstractions.Time;

/// <summary>
/// Abstracts the system clock so use cases can obtain the current time without
/// depending on <see cref="System.DateTime.UtcNow"/> directly. All values are UTC.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current Coordinated Universal Time (UTC).
    /// </summary>
    DateTime UtcNow { get; }
}
