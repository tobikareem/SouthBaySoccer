using FluentAssertions;
using SouthBaySoccer.Application.Features.Scheduling;
using Xunit;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class PickupPalOccurrenceKeyTests
{
    [Fact]
    public void Build_ThenTryGetGameId_RoundTrips()
    {
        var key = PickupPalOccurrenceKey.Build("cmrti8zc400fh75unavs2vrgi");

        PickupPalOccurrenceKey.TryGetGameId(key, out var gameId).Should().BeTrue();
        key.Should().Be("pickuppal:cmrti8zc400fh75unavs2vrgi");
        gameId.Should().Be("cmrti8zc400fh75unavs2vrgi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pickuppal:")]
    [InlineData("weekly:2026-09-16")]
    [InlineData("PICKUPPAL:game-1")]
    public void TryGetGameId_WhenKeyIsNotAnImportedSession_ReturnsFalse(string? occurrenceKey)
    {
        PickupPalOccurrenceKey.TryGetGameId(occurrenceKey, out var gameId).Should().BeFalse();

        gameId.Should().BeEmpty();
    }
}
