using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Infrastructure.Caching;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>
/// Pins the group-catalog cache contract. No database collection: the decorator is pure policy over
/// an injected clock and cache, so it verifies on any machine.
/// </summary>
public sealed class CachedPickupPalGroupClientTests
{
    private static readonly DateTime StartUtc = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAllGroupsAsync_WhenCalledRepeatedlyInsideTheFreshnessWindow_CallsTheProviderOnce()
    {
        var inner = CreateInner(out var callCount);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(StartUtc);
        var client = new CachedPickupPalGroupClient(inner.Object, NewCache(), clock.Object);

        await client.GetAllGroupsAsync();
        await client.GetAllGroupsAsync();
        var third = await client.GetAllGroupsAsync();

        callCount().Should().Be(1);
        third.Should().ContainSingle().Which.GroupName.Should().Be("Bay Area Soccer");
    }

    [Fact]
    public async Task GetAllGroupsAsync_WhenTheFreshnessWindowHasElapsed_RefetchesFromTheProvider()
    {
        var inner = CreateInner(out var callCount);
        var nowUtc = StartUtc;
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(() => nowUtc);
        var client = new CachedPickupPalGroupClient(inner.Object, NewCache(), clock.Object);

        await client.GetAllGroupsAsync();
        nowUtc = StartUtc.AddMinutes(6);
        await client.GetAllGroupsAsync();

        callCount().Should().Be(2);
    }

    [Fact]
    public async Task GetAllGroupsAsync_WhenTheProviderFailsAfterASuccessfulFetch_ServesTheStaleCatalog()
    {
        var inner = new Mock<IPickupPalGroupClient>();
        inner.SetupSequence(x => x.GetAllGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PickupPalGroupChat("120@g.us", "Bay Area Soccer", null, "SUBSCRIBED", 12, null)])
            .ThrowsAsync(new HttpRequestException("provider down"));
        var nowUtc = StartUtc;
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(() => nowUtc);
        var client = new CachedPickupPalGroupClient(inner.Object, NewCache(), clock.Object);

        await client.GetAllGroupsAsync();
        nowUtc = StartUtc.AddMinutes(6);
        var stale = await client.GetAllGroupsAsync();

        stale.Should().ContainSingle().Which.GroupName.Should().Be("Bay Area Soccer");
    }

    [Fact]
    public async Task GetAllGroupsAsync_WhenTheProviderFailsWithNothingCached_Throws()
    {
        var inner = new Mock<IPickupPalGroupClient>();
        inner.Setup(x => x.GetAllGroupsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider down"));
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(StartUtc);
        var client = new CachedPickupPalGroupClient(inner.Object, NewCache(), clock.Object);

        var act = () => client.GetAllGroupsAsync();

        await act.Should().ThrowAsync<HttpRequestException>(
            "with no catalog to fall back on the failure must surface, not be masked as an empty list");
    }

    [Fact]
    public async Task GetLinkedGroupsAsync_IsNeverCached()
    {
        var inner = new Mock<IPickupPalGroupClient>();
        inner.Setup(x => x.GetLinkedGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(StartUtc);
        var client = new CachedPickupPalGroupClient(inner.Object, NewCache(), clock.Object);

        await client.GetLinkedGroupsAsync("pp-user-1");
        await client.GetLinkedGroupsAsync("pp-user-1");

        // Per-player link state drives what a player may see, so it stays a live read.
        inner.Verify(x => x.GetLinkedGroupsAsync("pp-user-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static MemoryCache NewCache() => new(new MemoryCacheOptions());

    private static Mock<IPickupPalGroupClient> CreateInner(out Func<int> callCount)
    {
        var calls = 0;
        var inner = new Mock<IPickupPalGroupClient>();
        inner.Setup(x => x.GetAllGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                return [new PickupPalGroupChat("120@g.us", "Bay Area Soccer", null, "SUBSCRIBED", 12, null)];
            });
        callCount = () => calls;
        return inner;
    }
}
