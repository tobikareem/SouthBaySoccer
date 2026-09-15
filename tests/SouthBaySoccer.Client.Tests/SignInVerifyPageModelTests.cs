using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class SignInVerifyPageModelTests
{
    [Fact]
    public void Initialize_WithPendingSignIn_ShowsAccountAndReadsRememberDeviceFromFlow()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.SetupGet(f => f.LoginMessage).Returns("!!login n9jabay");
        flow.SetupProperty(f => f.RememberDevice, false);
        var pageModel = new SignInVerifyPageModel(flow.Object, new Mock<IOnboardingNavigator>().Object, new PickupPalOptions());

        pageModel.Initialize(new PhoneSignInStartResponse(true, "+1 (555) ••• 9421", "Ada Okafor", null));

        pageModel.Initials.Should().Be("AO");
        pageModel.AccountLine.Should().Contain("9421");
        pageModel.MessageText.Should().Be("!!login n9jabay");
        pageModel.RememberDevice.Should().BeFalse();
    }

    [Fact]
    public void RememberDevice_WhenToggled_PropagatesToFlow()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.SetupProperty(f => f.RememberDevice, true);
        var pageModel = new SignInVerifyPageModel(flow.Object, new Mock<IOnboardingNavigator>().Object, new PickupPalOptions());

        pageModel.RememberDevice = false;

        flow.Object.RememberDevice.Should().BeFalse();
    }

    [Fact]
    public async Task ContinueWithWhatsApp_HandoffThrows_IsRecoverable()
    {
        var flow = new Mock<IOnboardingFlow>();
        flow.Setup(f => f.StartSignInHandoffAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no stack"));
        var pageModel = new SignInVerifyPageModel(flow.Object, new Mock<IOnboardingNavigator>().Object, new PickupPalOptions());

        await pageModel.ContinueWithWhatsAppCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Be(SignInVerifyPageModel.HandoffFailedMessage);
        pageModel.IsBusy.Should().BeFalse();
    }
}
