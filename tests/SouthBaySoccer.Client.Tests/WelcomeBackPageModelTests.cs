using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class WelcomeBackPageModelTests
{
    [Fact]
    public async Task RequestPhoneSignIn_InvalidPhone_DoesNotCallClient()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);
        pageModel.PhoneNumber = "123";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.HasPhoneNumberError.Should().BeTrue();
        pageModel.IsBusy.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RequestPhoneSignIn_ValidPhone_NormalizesStoresTokensAndNavigates()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationTokensResponse(
                "access-token",
                "refresh-token",
                DateTime.UtcNow.AddMinutes(10)));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var authenticationCoordinator = CreateIncompleteCoordinator();
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.HasStatusMessage.Should().BeFalse();
        pageModel.IsBusy.Should().BeFalse();
        authenticationClient.Verify(
            client => client.SignInByPhoneAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()),
            Times.Once);
        authenticationCoordinator.Verify(
            coordinator => coordinator.CompleteSignInAsync(
                It.IsAny<AuthenticationTokensResponse>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestPhoneSignIn_Success_NavigatesWithoutStatusMessage()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationTokensResponse(
                "access-token",
                "refresh-token",
                DateTime.UtcNow.AddMinutes(10)));
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>();

        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.HasStatusMessage.Should().BeFalse();
        pageModel.IsBusy.Should().BeFalse();
        authenticationCoordinator.Verify(
            coordinator => coordinator.CompleteSignInAsync(
                It.IsAny<AuthenticationTokensResponse>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestPhoneSignIn_ServiceFailure_PreservesPhoneAndIsRecoverable()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            authenticationClient,
            CreateIncompleteCoordinator(),
            externalLauncher);
        pageModel.PhoneNumber = "+1 516 344 7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.PhoneNumber.Should().Be("+1 516 344 7233");
        pageModel.StatusMessage.Should().Contain("connection");
        pageModel.IsNotBusy.Should().BeTrue();
    }

    [Fact]
    public async Task RequestPhoneSignIn_NotFound_ShowsSignupPromptAndDoesNotNavigate()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("not found", null, System.Net.HttpStatusCode.NotFound));
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var dialogService = new Mock<IUserDialogService>();
        dialogService
            .Setup(dialog => dialog.ShowAlertAsync(
                WelcomeBackPageModel.PickupPalNotFoundTitle,
                WelcomeBackPageModel.PickupPalNotFoundMessage,
                "OK",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher,
            dialogService: dialogService);
        pageModel.PhoneNumber = "+1 516 344 7233";

        await pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);

        pageModel.HasStatusMessage.Should().BeFalse();
        pageModel.IsNotBusy.Should().BeTrue();
        WelcomeBackPageModel.PickupPalNotFoundMessage.Should().NotContain("516");
        dialogService.Verify(
            dialog => dialog.ShowAlertAsync(
                WelcomeBackPageModel.PickupPalNotFoundTitle,
                WelcomeBackPageModel.PickupPalNotFoundMessage,
                "OK",
                It.IsAny<CancellationToken>()),
            Times.Once);
        authenticationCoordinator.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task OpenPickupPalBot_LaunchFailure_ShowsRecoverableMessage()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalBotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);

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
            "Pickup Pal verifies your account. SouthBaySoccer stores only app session tokens on this device.");
        WelcomeBackPageModel.BotHelpMessage.Should().Be(
            "Need help? Open the Pickup Pal bot for account support.");
        WelcomeBackPageModel.SignupHelpMessage.Should().Be(
            "Create your account on the web, then come back and sign in with your phone number.");
    }

    [Fact]
    public async Task RequestPhoneSignIn_WhileInFlight_IsBusyAndCannotResubmit()
    {
        var gate = new TaskCompletionSource<AuthenticationTokensResponse>();
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()))
            .Returns(gate.Task);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var authenticationCoordinator = CreateIncompleteCoordinator();
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);
        pageModel.PhoneNumber = "+1 (516) 344-7233";

        var inFlight = pageModel.RequestWhatsAppChallengeCommand.ExecuteAsync(null);
        var busyDuringFlight = pageModel.IsBusy;
        var blockedDuringFlight = pageModel.RequestWhatsAppChallengeCommand.CanExecute(null);
        gate.SetResult(new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(10)));
        await inFlight;

        busyDuringFlight.Should().BeTrue();
        blockedDuringFlight.Should().BeFalse();
        pageModel.IsBusy.Should().BeFalse();
        pageModel.RequestWhatsAppChallengeCommand.CanExecute(null).Should().BeTrue();
        authenticationClient.Verify(
            client => client.SignInByPhoneAsync(
                "+15163447233",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestPhoneSignIn_UnexpectedFailure_IsRecoverableAndNonSensitive()
    {
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.SignInByPhoneAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var pageModel = CreatePageModel(
            authenticationClient,
            CreateIncompleteCoordinator(),
            externalLauncher);
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
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalBotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);

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
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);

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
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>();
        externalLauncher
            .Setup(launcher => launcher.OpenPickupPalSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher);

        await pageModel.OpenPickupPalSignupCommand.ExecuteAsync(null);

        pageModel.StatusMessage.Should().Contain("could not be opened");
        authenticationClient.VerifyNoOtherCalls();
    }

    [Fact]
    public void BotDisplayNumber_BindsFromTypedConfiguration_NotPageText()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var authenticationCoordinator = new Mock<IAuthenticationCoordinator>(MockBehavior.Strict);
        var externalLauncher = new Mock<IExternalLauncher>(MockBehavior.Strict);
        var options = new PickupPalOptions { BotDisplayNumber = "+1 (650) 111-2222" };
        var pageModel = CreatePageModel(
            authenticationClient,
            authenticationCoordinator,
            externalLauncher,
            options);

        pageModel.BotDisplayNumber.Should().Be("+1 (650) 111-2222");
    }

    private static WelcomeBackPageModel CreatePageModel(
        Mock<IAuthenticationClient> authenticationClient,
        Mock<IAuthenticationCoordinator> authenticationCoordinator,
        Mock<IExternalLauncher> externalLauncher,
        PickupPalOptions? options = null,
        Mock<IUserDialogService>? dialogService = null) =>
        new(
            authenticationClient.Object,
            authenticationCoordinator.Object,
            externalLauncher.Object,
            (dialogService ?? new Mock<IUserDialogService>(MockBehavior.Strict)).Object,
            options ?? new PickupPalOptions());

    private static Mock<IAuthenticationCoordinator> CreateIncompleteCoordinator()
    {
        var coordinator = new Mock<IAuthenticationCoordinator>();
        coordinator
            .Setup(item => item.TryCompleteChallengeAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        coordinator
            .Setup(item => item.CompleteSignInAsync(
                It.IsAny<AuthenticationTokensResponse>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return coordinator;
    }
}

