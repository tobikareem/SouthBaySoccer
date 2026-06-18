using FluentAssertions;
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

        options.SignupUri.Scheme.Should().Be("https");
        options.BotUri.Host.Should().Be("wa.me");
        options.CallbackUri.Scheme.Should().Be("southbaysoccer");
        options.BotDisplayNumber.Should().NotBeNullOrWhiteSpace();
    }
}
