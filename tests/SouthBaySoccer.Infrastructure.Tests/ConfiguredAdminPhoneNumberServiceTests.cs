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

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
}
