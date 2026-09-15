using FluentAssertions;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Infrastructure.Authentication.Onboarding;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class ConfiguredOnboardingPolicyTests
{
    [Theory]
    [InlineData("+15550001234", true)]
    [InlineData("1 (555) 000-1234", true)]
    [InlineData("+15550009999", true)]
    [InlineData("+15550005555", false)]
    [InlineData("", false)]
    public void IsVerificationExempt_WhenNumberConfiguredInAnyFormat_MatchesOnNormalizedDigits(string phoneNumber, bool expected)
    {
        var policy = CreatePolicy(new OnboardingOptions
        {
            VerificationExemptPhoneNumbers = " +1 (555) 000-1234 , 15550009999 ,, ",
        });

        policy.IsVerificationExempt(phoneNumber).Should().Be(expected);
    }

    [Fact]
    public void Defaults_WhenNothingConfigured_RequireVerificationAndServeCurrentTerms()
    {
        var policy = CreatePolicy(new OnboardingOptions());

        policy.RequireWhatsAppVerification.Should().BeTrue();
        policy.TermsVersion.Should().Be("20250708");
        policy.PendingSignInLifetime.Should().Be(TimeSpan.FromMinutes(15));
        policy.RememberDeviceRefreshTokenLifetime.Should().Be(TimeSpan.FromDays(30));
        policy.SessionRefreshTokenLifetime.Should().Be(TimeSpan.FromHours(12));
        // Remember-device is only meaningful when the session lifetime is materially shorter.
        policy.SessionRefreshTokenLifetime.Should().BeLessThan(policy.RememberDeviceRefreshTokenLifetime);
        policy.IsVerificationExempt("+15550001234").Should().BeFalse();
    }

    private static ConfiguredOnboardingPolicy CreatePolicy(OnboardingOptions options) =>
        new(Options.Create(options));
}
