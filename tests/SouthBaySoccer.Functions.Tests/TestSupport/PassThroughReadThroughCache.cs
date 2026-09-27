using SouthBaySoccer.Application.Abstractions.Caching;

namespace SouthBaySoccer.Functions.Tests.TestSupport;

/// <summary>
/// A cache that never caches: every call runs the factory. Handler tests assert what the handler
/// asks its repositories for, so memoization must not hide those calls. Cache behaviour itself is
/// covered by the cache implementation's own tests.
/// </summary>
public sealed class PassThroughReadThroughCache : IReadThroughCache
{
    public Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default) =>
        factory(cancellationToken);
}
