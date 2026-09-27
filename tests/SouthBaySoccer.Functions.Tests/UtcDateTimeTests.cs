using FluentAssertions;
using SouthBaySoccer.Functions.Onboarding;

namespace SouthBaySoccer.Functions.Tests;

public sealed class UtcDateTimeTests
{
    [Fact]
    public void Normalize_WhenValueIsUtc_ReturnsItUnchanged()
    {
        var value = new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);

        var normalized = UtcDateTime.Normalize(value);

        normalized.Should().Be(value);
        normalized.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Normalize_WhenValueIsUnspecified_TreatsItAsUtcWithoutShifting()
    {
        var value = new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Unspecified);

        var normalized = UtcDateTime.Normalize(value);

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Ticks.Should().Be(value.Ticks);
    }

    [Fact]
    public void Normalize_WhenValueIsLocal_ConvertsToUtc()
    {
        var value = new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Local);

        var normalized = UtcDateTime.Normalize(value);

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(value.ToUniversalTime());
    }
}
