using FluentAssertions;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class CorrelationIdProviderTests
{
    private readonly CorrelationIdProvider _provider = new();

    [Fact]
    public void Resolve_WhenHeaderValueIsSafe_ReturnsHeaderValue()
    {
        var correlationId = _provider.Resolve(["mobile-123:abc"]);

        correlationId.Should().Be("mobile-123:abc");
    }

    [Fact]
    public void Resolve_WhenHeaderValueIsMissing_GeneratesCorrelationId()
    {
        var correlationId = _provider.Resolve(null);

        correlationId.Should().NotBeNullOrWhiteSpace();
        correlationId.Should().HaveLength(32);
    }

    [Fact]
    public void Resolve_WhenHeaderValueContainsUnsafeCharacters_GeneratesCorrelationId()
    {
        var correlationId = _provider.Resolve(["bad\r\nx-secret: leaked"]);

        correlationId.Should().NotBe("bad\r\nx-secret: leaked");
        correlationId.Should().HaveLength(32);
    }

    [Fact]
    public void Resolve_WhenHeaderValueIsTooLong_GeneratesCorrelationId()
    {
        var correlationId = _provider.Resolve([new string('a', 129)]);

        correlationId.Should().HaveLength(32);
    }
}
