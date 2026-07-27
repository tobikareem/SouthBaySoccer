using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Infrastructure.Caching;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class MemoryReadThroughCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenTheKeyIsCached_DoesNotRunTheFactoryAgain()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryReadThroughCache(memoryCache);
        var calls = 0;

        await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), _ => { calls++; return Task.FromResult("value"); });
        var second = await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), _ => { calls++; return Task.FromResult("other"); });

        calls.Should().Be(1);
        second.Should().Be("value");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenKeysDiffer_CachesThemIndependently()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryReadThroughCache(memoryCache);

        var first = await cache.GetOrCreateAsync("a", TimeSpan.FromMinutes(1), _ => Task.FromResult("first"));
        var second = await cache.GetOrCreateAsync("b", TimeSpan.FromMinutes(1), _ => Task.FromResult("second"));

        first.Should().Be("first");
        second.Should().Be("second");
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenTheFactoryThrows_CachesNothing()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryReadThroughCache(memoryCache);

        var act = () => cache.GetOrCreateAsync<string>("k", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException("boom"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        var afterFailure = await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), _ => Task.FromResult("recovered"));

        afterFailure.Should().Be("recovered", "a failed read must not poison the key");
    }
}
