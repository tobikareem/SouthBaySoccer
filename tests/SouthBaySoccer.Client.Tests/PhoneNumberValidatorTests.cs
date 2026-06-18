using FluentAssertions;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class PhoneNumberValidatorTests
{
    [Theory]
    [InlineData("+1 (516) 344-7233", "+15163447233")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    public void TryNormalize_ValidInternationalNumber_ReturnsE164Digits(
        string input,
        string expected)
    {
        var result = PhoneNumberValidator.TryNormalize(input, out var normalized);

        result.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("not a phone")]
    [InlineData("+1 call 516 344 7233")]
    public void TryNormalize_InvalidNumber_ReturnsFalse(string input)
    {
        var result = PhoneNumberValidator.TryNormalize(input, out var normalized);

        result.Should().BeFalse();
        normalized.Should().BeEmpty();
    }
}
