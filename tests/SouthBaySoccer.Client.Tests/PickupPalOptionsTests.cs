using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SouthBaySoccer.Configuration;

namespace SouthBaySoccer.Client.Tests;

public class PickupPalOptionsTests
{
    // AUTH-9: external actions launch from typed configuration, signup is HTTPS, and the
    // deep-link callback uses the approved custom scheme (not duplicated page text).
    [Fact]
    public void Defaults_UseHttpsSignupWhatsAppBotAndApprovedCallbackScheme()
    {
        var options = new PickupPalOptions();

        options.ApiBaseUri.ToString().Should().Be("http://localhost:7071/api/");
        options.SignupUri.Scheme.Should().Be("https");
        options.BotUri.Host.Should().Be("wa.me");
        options.CallbackUri.Scheme.Should().Be("southbaysoccer");
        options.BotDisplayNumber.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FromConfiguration_WhenProductionApiBaseUrlConfigured_UsesConfiguredHostWithFunctionsApiPrefix()
    {
        var configuration = Configuration([
            new(PickupPalOptions.ProductionApiBaseUrlKey, "https://carepath-api.test"),
        ]);

        var options = PickupPalOptions.FromConfiguration(configuration);

        options.ApiBaseUri.ToString().Should().Be("https://carepath-api.test/api/");
    }

    [Fact]
    public void FromConfiguration_WhenProductionApiBaseUrlIsDeployedFunctionsHost_UsesFunctionsApiRoot()
    {
        var configuration = Configuration([
            new(PickupPalOptions.ProductionApiBaseUrlKey, "https://carepath-api-hvhxgvhxejc0fmg3.westus2-01.azurewebsites.net"),
        ]);

        var options = PickupPalOptions.FromConfiguration(configuration);

        options.ApiBaseUri.ToString().Should()
            .Be("https://carepath-api-hvhxgvhxejc0fmg3.westus2-01.azurewebsites.net/api/");
    }

    [Fact]
    public void FromConfiguration_WhenApiBaseUrlIncludesPath_PreservesConfiguredPathWithTrailingSlash()
    {
        var configuration = Configuration([
            new("PickupPal:ApiBaseUrl", "https://api.test/mobile"),
        ]);

        var options = PickupPalOptions.FromConfiguration(configuration);

        options.ApiBaseUri.ToString().Should().Be("https://api.test/mobile/");
    }

    [Fact]
    public void FromConfiguration_WhenNoApiBaseUrlConfigured_UsesSuppliedDefault()
    {
        var configuration = Configuration([]);

        var options = PickupPalOptions.FromConfiguration(
            configuration,
            PickupPalOptions.AndroidDebugApiBaseUrl);

        options.ApiBaseUri.ToString().Should().Be("http://10.0.2.2:7071/api/");
    }

    [Fact]
    public void FromConfiguration_WhenApiBaseUrlIsInvalid_FailsFast()
    {
        var configuration = Configuration([
            new(PickupPalOptions.ProductionApiBaseUrlKey, "not a url"),
        ]);

        var act = () => PickupPalOptions.FromConfiguration(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiBaseUri*absolute URI*");
    }

    private static IConfiguration Configuration(IReadOnlyList<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
