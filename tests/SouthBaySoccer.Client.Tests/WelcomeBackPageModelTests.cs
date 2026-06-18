using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class WelcomeBackPageModelTests
{
    [Fact]
    public async Task RequestWhatsAppChallenge_InvalidPhone_DoesNotCallClient()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);
        pageModel.PhoneNumber = "123";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.HasPhoneNumberError.Should().BeTrue();
        pageModel.IsBusy.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequestWhatsAppChallenge_ValidPhone_NormalizesAndCallsOnce()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RequestWhatsAppChallengeAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestWhatsAppChallengeResponse(
                "challenge-id",
                DateTime.UtcNow.AddMinutes(10)));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Contain("Check WhatsApp");
        pageModel.IsBusy.Should().BeFalse();
        authenticationClient.Verify(
            client => client.RequestWhatsAppChallengeAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWhatsAppChallenge_ServiceFailure_PreservesPhoneAndIsRecoverable()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RequestWhatsAppChallengeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);
        pageModel.PhoneNumber = "+1 516 344 7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.PhoneNumber.Should().Be("+1 516 344 7233");
        pageModel.StatusMessage.Should().Contain("connection");
        pageModel.IsNotBusy.Should().BeTrue();
    }

    [Fact]
    public async Task OpenPickupPalBot_LaunchFailure_ShowsRecoverableMessage()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalBotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);

        await pageModel.OpenPickupPalBotCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Contain("could not be opened");
    }

    [Fact]
    public void WireframeCopy_ExposedByPageModel_MatchesAuth7Specification()
    {
        WelcomeBackPageModel.WelcomeLabel.Should().Be("WELCOME BACK");
        WelcomeBackPageModel.Heading.Should().Be("Your next game starts here.");
        WelcomeBackPageModel.SecurityHeading.Should().Be("Password-free and secure");
        WelcomeBackPageModel.SecurityMessage.Should().Be(
            "We use a one-time link through WhatsApp. SouthBaySoccer never sees your WhatsApp password.");
        WelcomeBackPageModel.BotHelpMessage.Should().Be(
            "Or message the bot a link code to connect this device.");
        WelcomeBackPageModel.SignupHelpMessage.Should().Be(
            "Create your account on the web, then come back and continue with WhatsApp.");
    }

    [Fact]
    public async Task RequestWhatsAppChallenge_WhileInFlight_IsBusyAndCannotResubmit()
    {
        var gate = new TaskCompletionSource<RequestWhatsAppChallengeResponse>();
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RequestWhatsAppChallengeAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        var inFlight = pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);
        var busyDuringFlight = pageModel.IsBusy;
        var blockedDuringFlight = pageModel.RequestWhatsAppChallengeCommand.CanExecute(null);
        gate.SetResult(new RequestWhatsAppChallengeResponse(
            "challenge-id",
            DateTime.UtcNow.AddMinutes(10)));
        await inFlight;

        busyDuringFlight.Should().BeTrue();
        blockedDuringFlight.Should().BeFalse();
        pageModel.IsBusy.Should().BeFalse();
        pageModel.RequestWhatsAppChallengeCommand.CanExecute(null).Should().BeTrue();
        authenticationClient.Verify(
            client => client.RequestWhatsAppChallengeAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestWhatsAppChallenge_UnexpectedFailure_IsRecoverableAndNonSensitive()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.RequestWhatsAppChallengeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.PhoneNumber.Should().Be("+1 (516) 344-7233");
        pageModel.IsNotBusy.Should().BeTrue();
        pageModel.StatusMessage.Should().Contain("try again");
        pageModel.StatusMessage.Should().NotContain("516", "error copy must not leak the phone number");
    }

    [Fact]
    public async Task OpenPickupPalBot_LaunchSucceeds_OpensBotWithoutError()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalBotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);

        await pageModel.OpenPickupPalBotCommand.ExecuteAsync(null);

        pageModel.HasStatusMessage.Should().BeFalse();
        externalLauncher.Verify(
            launcher => launcher.OpenPickupPalBotAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OpenPickupPalSignup_LaunchSucceeds_OpensSignupWithoutError()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);

        await pageModel.OpenPickupPalSignupCommand.ExecuteAsync(null);

        pageModel.HasStatusMessage.Should().BeFalse();
        externalLauncher.Verify(
            launcher => launcher.OpenPickupPalSignupAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OpenPickupPalSignup_LaunchFailure_ShowsRecoverableMessageAndStays()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = CreatePageModel(authenticationClient, externalLauncher);

        await pageModel.OpenPickupPalSignupCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Contain("could not be opened");
        authenticationClient.VerifyNoOtherCalls();
    }

    [Fact]
    public void BotDisplayNumber_BindsFromTypedConfiguration_NotPageText()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var options = new PickupPalOptions { BotDisplayNumber = "+1 (650) 111-2222" };
        var pageModel = CreatePageModel(authenticationClient, externalLauncher, options);

        pageModel.BotDisplayNumber.Should().Be("+1 (650) 111-2222");
    }

    private static WelcomeBackPageModel CreatePageModel(
        Mock<IAuthenticationClient> authenticationClient,
        Mock<IExternalLauncher> externalLauncher,
        PickupPalOptions? options = null) =>
        new(authenticationClient.Object, externalLauncher.Object, options ?? new PickupPalOptions());
}
