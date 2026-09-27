using FluentAssertions;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public sealed class ClientResponseCacheTests
{
    /// <summary>Minimal controllable clock; the test project does not reference the time-testing package.</summary>
    private sealed class AdvanceableTimeProvider : TimeProvider
    {
        private DateTimeOffset nowUtc = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => nowUtc;

        public void Advance(TimeSpan delta) => nowUtc = nowUtc.Add(delta);
    }

    [Fact]
    public async Task GetOrCreateAsync_InsideTheFreshnessWindow_RunsTheFactoryOnce()
    {
        var cache = new ClientResponseCache(new AdvanceableTimeProvider());
        var calls = 0;

        await cache.GetOrCreateAsync("k", TimeSpan.FromSeconds(30), _ => { calls++; return Task.FromResult("v"); });
        var second = await cache.GetOrCreateAsync("k", TimeSpan.FromSeconds(30), _ => { calls++; return Task.FromResult("other"); });

        calls.Should().Be(1);
        second.Should().Be("v");
    }

    [Fact]
    public async Task GetOrCreateAsync_AfterTheWindowElapses_RunsTheFactoryAgain()
    {
        var time = new AdvanceableTimeProvider();
        var cache = new ClientResponseCache(time);
        var calls = 0;

        await cache.GetOrCreateAsync("k", TimeSpan.FromSeconds(30), _ => { calls++; return Task.FromResult("v"); });
        time.Advance(TimeSpan.FromSeconds(31));
        await cache.GetOrCreateAsync("k", TimeSpan.FromSeconds(30), _ => { calls++; return Task.FromResult("v2"); });

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_RemovesEveryKeyUnderThePrefixAndLeavesOthers()
    {
        var cache = new ClientResponseCache(new AdvanceableTimeProvider());
        await cache.GetOrCreateAsync("sessions:dashboard", TimeSpan.FromMinutes(5), _ => Task.FromResult("a"));
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => Task.FromResult("b"));

        cache.Invalidate("sessions:");
        var sessionsCalls = 0;
        var profileCalls = 0;
        await cache.GetOrCreateAsync("sessions:dashboard", TimeSpan.FromMinutes(5), _ => { sessionsCalls++; return Task.FromResult("a2"); });
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => { profileCalls++; return Task.FromResult("b2"); });

        sessionsCalls.Should().Be(1);
        profileCalls.Should().Be(0, "an unrelated prefix must survive invalidation");
    }

    [Fact]
    public async Task Clear_DropsEverything()
    {
        var cache = new ClientResponseCache(new AdvanceableTimeProvider());
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => Task.FromResult("b"));

        cache.Clear();
        var calls = 0;
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => { calls++; return Task.FromResult("b2"); });

        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenClearRunsWhileTheFactoryIsInFlight_DoesNotWriteTheResultBack()
    {
        var cache = new ClientResponseCache(new AdvanceableTimeProvider());
        var release = new TaskCompletionSource();

        var inFlight = cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), async _ =>
        {
            await release.Task;
            return "previous account";
        });
        cache.Clear();
        release.SetResult();
        await inFlight;

        var calls = 0;
        await cache.GetOrCreateAsync("profile:me", TimeSpan.FromMinutes(5), _ => { calls++; return Task.FromResult("next"); });
        calls.Should().Be(1, "a response already in flight at sign-out must not re-seed the cache after it");
    }
}
