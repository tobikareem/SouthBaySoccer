using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Tests;

public sealed class ConfiguredAdminPhoneNumberServiceTests
{
    [Fact]
    public void IsConfiguredAdminPhoneNumber_WhenCommaSeparatedNumbersConfigured_NormalizesDigits()
    {
        var service = new ConfiguredAdminPhoneNumberService(
            Options.Create(new AdminPhoneNumberOptions
            {
                AdminPhoneNumbers = "15163447233, 1 (650) 602-3417,",
            }));

        service.IsConfiguredAdminPhoneNumber("+1 (516) 344-7233").Should().BeTrue();
        service.IsConfiguredAdminPhoneNumber("16506023417").Should().BeTrue();
        service.IsConfiguredAdminPhoneNumber("13105550123").Should().BeFalse();
    }

    [Fact]
    public void IsConfiguredAdminPhoneNumberHash_WhenHashMatchesNormalizedNumber_ReturnsTrue()
    {
        var service = new ConfiguredAdminPhoneNumberService(
            Options.Create(new AdminPhoneNumberOptions
            {
                AdminPhoneNumbers = "15163447233",
            }));
        var hash = Sha256("+15163447233");

        service.IsConfiguredAdminPhoneNumberHash(hash).Should().BeTrue();
    }

    [Fact]
    public void IsConfiguredOwnerPhoneNumber_WhenOwnerNumbersConfigured_MatchesNormalizedDigitsAndHashesOnly()
    {
        var service = new ConfiguredAdminPhoneNumberService(
            Options.Create(new AdminPhoneNumberOptions
            {
                AdminPhoneNumbers = "15163447233",
                OwnerPhoneNumbers = "1 (650) 602-3417, 13105550123",
            }));

        service.IsConfiguredOwnerPhoneNumber("+1 650-602-3417").Should().BeTrue();
        service.IsConfiguredOwnerPhoneNumberHash(Sha256("+13105550123")).Should().BeTrue();
        service.IsConfiguredOwnerPhoneNumber("15163447233").Should().BeFalse("an admin number is not an owner number");
        service.IsConfiguredAdminPhoneNumber("16506023417").Should().BeFalse("the lists are independent");
        service.IsConfiguredOwnerPhoneNumberHash(null).Should().BeFalse();
    }

    [Fact]
    public void IsConfiguredOwnerPhoneNumber_WhenNothingConfigured_ReturnsFalse()
    {
        var service = new ConfiguredAdminPhoneNumberService(Options.Create(new AdminPhoneNumberOptions()));

        service.IsConfiguredOwnerPhoneNumber("16506023417").Should().BeFalse();
        service.IsConfiguredOwnerPhoneNumberHash(Sha256("+16506023417")).Should().BeFalse();
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
}
