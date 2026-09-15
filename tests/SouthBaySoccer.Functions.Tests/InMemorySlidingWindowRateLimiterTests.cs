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

    [Theory]
    [InlineData(" Andre@Example.com ", "andre@example.com")]
    [InlineData("+1 (510) 694-9421", "+1 (510) 694-9421")]
    public void InputKey_WhenInputVariesByCaseOrWhitespace_ProducesSameHashAsNormalizedValue(string input, string normalized)
    {
        var key = AnonymousRateLimits.InputKey(input);

        key.Should().Be(AnonymousRateLimits.InputKey(normalized));
        key.Should().HaveLength(64).And.NotContainEquivalentOf("example");
    }

    private static (InMemorySlidingWindowRateLimiter Limiter, Mock<IClock> Clock) CreateLimiter()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Start);
        return (new InMemorySlidingWindowRateLimiter(clock.Object), clock);
    }
}
