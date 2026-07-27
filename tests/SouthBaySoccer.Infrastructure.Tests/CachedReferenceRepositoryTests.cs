using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Caching;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>
/// Pins the reference-data cache contract, including the rule that an invalidated key is only
/// evicted once the writing request commits. No database collection: these are pure policy tests.
/// </summary>
public sealed class CachedReferenceRepositoryTests
{
    [Fact]
    public async Task ListActiveAsync_WhenCalledTwice_ReadsTheInnerRepositoryOnce()
    {
        var inner = new Mock<IVenueRepository>();
        inner.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Venue { Id = Guid.NewGuid(), Name = "Marina Field", Locality = "Torrance" }]);
        using var cache = NewCache();
        var repository = new CachedVenueRepository(inner.Object, cache, new CacheEvictionQueue(cache));

        await repository.ListActiveAsync();
        await repository.ListActiveAsync();

        inner.Verify(x => x.ListActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_BeforeTheCommitFlushes_LeavesTheCachedListInPlace()
    {
        var inner = new Mock<IVenueRepository>();
        inner.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Venue { Id = Guid.NewGuid(), Name = "Marina Field", Locality = "Torrance" }]);
        using var cache = NewCache();
        var evictionQueue = new CacheEvictionQueue(cache);
        var repository = new CachedVenueRepository(inner.Object, cache, evictionQueue);
        await repository.ListActiveAsync();

        await repository.AddAsync(new Venue { Id = Guid.NewGuid(), Name = "New Field", Locality = "Torrance" });
        await repository.ListActiveAsync();

        // The write has not committed yet, so the cache must not be repopulated from a state the
        // database has not accepted; eviction happens when the unit of work flushes.
        inner.Verify(x => x.ListActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddAsync_AfterTheCommitFlushes_RereadsTheList()
    {
        var inner = new Mock<IVenueRepository>();
        inner.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Venue { Id = Guid.NewGuid(), Name = "Marina Field", Locality = "Torrance" }]);
        using var cache = NewCache();
        var evictionQueue = new CacheEvictionQueue(cache);
        var repository = new CachedVenueRepository(inner.Object, cache, evictionQueue);
        await repository.ListActiveAsync();

        await repository.AddAsync(new Venue { Id = Guid.NewGuid(), Name = "New Field", Locality = "Torrance" });
        evictionQueue.Flush();
        await repository.ListActiveAsync();

        inner.Verify(x => x.ListActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SeasonUpdate_AfterFlush_InvalidatesTheActiveSeasonList()
    {
        var inner = new Mock<ISeasonRepository>();
        inner.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Season { Id = Guid.NewGuid(), Name = "Summer 2026" }]);
        using var cache = NewCache();
        var evictionQueue = new CacheEvictionQueue(cache);
        var repository = new CachedSeasonRepository(inner.Object, cache, evictionQueue);
        await repository.ListActiveAsync();

        repository.Update(new Season { Id = Guid.NewGuid(), Name = "Summer 2026" });
        evictionQueue.Flush();
        await repository.ListActiveAsync();

        inner.Verify(x => x.ListActiveAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetByIdAsync_IsNeverServedFromTheListCache()
    {
        var venueId = Guid.NewGuid();
        var inner = new Mock<IVenueRepository>();
        inner.Setup(x => x.GetByIdAsync(venueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Venue { Id = venueId, Name = "Marina Field", Locality = "Torrance" });
        using var cache = NewCache();
        var repository = new CachedVenueRepository(inner.Object, cache, new CacheEvictionQueue(cache));

        await repository.GetByIdAsync(venueId);
        await repository.GetByIdAsync(venueId);

        inner.Verify(x => x.GetByIdAsync(venueId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static MemoryCache NewCache() => new(new MemoryCacheOptions());
}
