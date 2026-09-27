namespace SouthBaySoccer.Services.Clients;

/// <summary>Result of a revision-aware read; unchanged responses intentionally carry no payload.</summary>
public readonly record struct ConditionalReadResult<T>(bool Changed, long Revision, T? Value, string? Validator = null)
    where T : class;

/// <summary>Page-lifetime delay seam used by adaptive polling and deterministic tests.</summary>
public interface IPollingDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Adds a small random spread so devices do not poll the service in lockstep.</summary>
public sealed class JitteredPollingDelay : IPollingDelay
{
    private const int JitterMilliseconds = 250;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var jitter = Random.Shared.Next(-JitterMilliseconds, JitterMilliseconds + 1);
        var effectiveDelay = delay + TimeSpan.FromMilliseconds(jitter);
        return Task.Delay(effectiveDelay > TimeSpan.Zero ? effectiveDelay : TimeSpan.Zero, cancellationToken);
    }
}
