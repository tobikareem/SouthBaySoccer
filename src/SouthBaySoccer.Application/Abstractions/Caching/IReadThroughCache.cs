namespace SouthBaySoccer.Application.Abstractions.Caching;

/// <summary>
/// Short-lived read-through memoization for expensive derived reads.
/// </summary>
/// <remarks>
/// This is response memoization, not a projection store: raw facts remain the only source of truth,
/// entries expire on a timer rather than being maintained, and nothing writes through it. It must
/// never be used for capacity decisions, compliance gates, payment state, or auth material — those
/// read live every time. Cache keys carry internal identifiers only, never phone numbers, WhatsApp
/// ids, or any other personal identifier.
/// </remarks>
public interface IReadThroughCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="factory"/>
    /// and caches its result for <paramref name="timeToLive"/>.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);
}
