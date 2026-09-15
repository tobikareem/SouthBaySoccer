using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Pipeline.RateLimiting;

namespace SouthBaySoccer.Functions.Tests;

public sealed class InMemorySlidingWindowRateLimiterTests
{
    private static readonly DateTime Start = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);
    private static readonly RateLimitRule Rule = new("test", 3, TimeSpan.FromMinutes(5));

    [Fact]
    public void EnsureAllowed_WhenUnderLimit_AllowsEveryHit()
    {
        var (limiter, _) = CreateLimiter();

        var act = () =>
        {
            for (var i = 0; i < Rule.Limit; i++)
            {
                limiter.EnsureAllowed(Rule, "key");
            }
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowed_WhenWindowFull_ThrowsRateLimitExceeded()
    {
        var (limiter, _) = CreateLimiter();
        for (var i = 0; i < Rule.Limit; i++)
        {
            limiter.EnsureAllowed(Rule, "key");
        }

        var act = () => limiter.EnsureAllowed(Rule, "key");

        act.Should().Throw<RateLimitExceededException>();
    }

    [Fact]
    public void EnsureAllowed_WhenWindowSlidesPastOldestHit_AllowsAgain()
    {
        var (limiter, clock) = CreateLimiter();
        for (var i = 0; i < Rule.Limit; i++)
        {
            limiter.EnsureAllowed(Rule, "key");
        }
        clock.SetupGet(x => x.UtcNow).Returns(Start.Add(Rule.Window).AddSeconds(1));

        var act = () => limiter.EnsureAllowed(Rule, "key");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowed_WhenKeysDiffer_CountsThemSeparately()
    {
        var (limiter, _) = CreateLimiter();
        for (var i = 0; i < Rule.Limit; i++)
        {
            limiter.EnsureAllowed(Rule, "key-a");
        }

        var act = () => limiter.EnsureAllowed(Rule, "key-b");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureAllowed_WhenScopesDifferForSameKey_CountsThemSeparately()
    {
        var (limiter, _) = CreateLimiter();
        var otherScope = Rule with { Scope = "other" };
        for (var i = 0; i < Rule.Limit; i++)
        {
            limiter.EnsureAllowed(Rule, "key");
        }

        var act = () => limiter.EnsureAllowed(otherScope, "key");

        act.Should().NotThrow();
    }

    [Fact]
    public void InputKey_WhenEmailVariesByCaseOrWhitespace_ProducesSameHashAsNormalizedValue()
    {
        var key = AnonymousRateLimits.InputKey(" Andre@Example.com ");

        key.Should().Be(AnonymousRateLimits.InputKey("andre@example.com"));
        key.Should().HaveLength(64).And.NotContainEquivalentOf("example");
    }

    [Theory]
    [InlineData("(510) 694-9421")]
    [InlineData("+1 510 694 9421")]
    [InlineData("15106949421")]
    [InlineData("5106949421")]
    public void PhoneKey_WhenSameNumberIsTypedDifferently_ProducesTheKeyOfTheNormalizedDigits(string input)
    {
        // The per-phone limit must follow the number Pickup Pal is asked about, not the raw text,
        // or a caller could dodge it by re-formatting the same number.
        var key = AnonymousRateLimits.PhoneKey(input);

        key.Should().Be(AnonymousRateLimits.PhoneKey("+15106949421"));
        key.Should().HaveLength(64).And.NotContain("9421");
    }

    [Theory]
    [InlineData("203.0.113.9", null, "203.0.113.9")]
    [InlineData("203.0.113.9", "198.51.100.1, 10.0.0.1", "203.0.113.9")]
    [InlineData(null, "198.51.100.1, 10.0.0.2, 10.0.0.3", "10.0.0.3")]
    [InlineData(null, "198.51.100.1:5123", "198.51.100.1")]
    [InlineData(null, "spoofed, 198.51.100.1:5123", "198.51.100.1")]
    [InlineData(null, "[2001:db8::1]:5123", "2001:db8::1")]
    [InlineData("[2001:db8::2]:443", null, "2001:db8::2")]
    [InlineData(null, "2001:db8::3", "2001:db8::3")]
    public void ClientIpKey_WhenProxyHeadersVary_PrefersAzureClientIpThenLastForwardedHopWithoutPort(
        string? azureClientIp,
        string? forwardedFor,
        string expectedAddress)
    {
        var key = AnonymousRateLimits.ClientIpKey(azureClientIp, forwardedFor);

        key.Should().Be(AnonymousRateLimits.ClientIpKey(expectedAddress, null));
        key.Should().HaveLength(64);
    }

    [Fact]
    public void ClientIpKey_WhenNoHeaders_SharesTheUnknownBucket()
    {
        AnonymousRateLimits.ClientIpKey(null, null).Should().Be(AnonymousRateLimits.ClientIpKey("", " "));
    }

    private static (InMemorySlidingWindowRateLimiter Limiter, Mock<IClock> Clock) CreateLimiter()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Start);
        return (new InMemorySlidingWindowRateLimiter(clock.Object), clock);
    }
}
