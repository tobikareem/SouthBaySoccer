using FluentAssertions;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;

namespace SouthBaySoccer.Domain.Tests;

public sealed class PendingPhoneSignInTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsActiveAt_WhenUnconsumedAndBeforeExpiry_ReturnsTrue()
    {
        var pending = new PendingPhoneSignIn { ExpiresAtUtc = Now.AddMinutes(1) };

        pending.IsActiveAt(Now).Should().BeTrue();
    }

    [Fact]
    public void IsActiveAt_WhenExactlyAtExpiry_ReturnsFalse()
    {
        var pending = new PendingPhoneSignIn { ExpiresAtUtc = Now };

        pending.IsActiveAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsActiveAt_WhenConsumed_ReturnsFalse()
    {
        var pending = new PendingPhoneSignIn { ExpiresAtUtc = Now.AddMinutes(10), ConsumedAtUtc = Now.AddMinutes(-1) };

        pending.IsActiveAt(Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(PlayerRegistrationStatus.PendingExternal, true)]
    [InlineData(PlayerRegistrationStatus.ExternalFailed, true)]
    [InlineData(PlayerRegistrationStatus.Completed, false)]
    public void IsAwaitingExternal_WhenStatusVaries_ReflectsWhetherPickupPalStillNeedsTheAccount(
        PlayerRegistrationStatus status,
        bool expected)
    {
        var registration = new PlayerRegistration { Status = status };

        registration.IsAwaitingExternal.Should().Be(expected);
    }
}
