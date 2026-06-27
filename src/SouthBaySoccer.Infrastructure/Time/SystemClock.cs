using SouthBaySoccer.Application.Abstractions.Time;

namespace SouthBaySoccer.Infrastructure.Time;

/// <summary>
/// Production UTC clock implementation.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
