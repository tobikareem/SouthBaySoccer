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

    private static WelcomeBackPageModel CreatePageModel(
        Mock<IAuthenticationClient> authenticationClient,
        Mock<IExternalLauncher> externalLauncher) =>
        new(authenticationClient.Object, externalLauncher.Object, new PickupPalOptions());
}
