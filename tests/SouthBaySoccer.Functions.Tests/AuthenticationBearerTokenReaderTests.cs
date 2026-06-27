using FluentAssertions;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class AuthenticationBearerTokenReaderTests
{
    [Fact]
    public void TryRead_WhenAuthorizationHeaderContainsBearerToken_ReturnsTokenOnly()
    {
        var headers = new HttpHeadersCollection
        {
            { "Authorization", "Bearer access-token" },
        };

        var token = BearerTokenReader.TryRead(headers);

        token.Should().Be("access-token");
    }

    [Theory]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    public void TryRead_WhenAuthorizationHeaderIsNotUsable_ReturnsNull(string value)
    {
        var headers = new HttpHeadersCollection
        {
            { "Authorization", value },
        };

        var token = BearerTokenReader.TryRead(headers);

        token.Should().BeNull();
    }

    [Fact]
    public void TryRead_WhenAuthorizationHeaderMissing_ReturnsNull()
    {
        var headers = new HttpHeadersCollection();

        var token = BearerTokenReader.TryRead(headers);

        token.Should().BeNull();
    }
}
