using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

public class AuthenticationCoordinatorTests
{
    [Fact]
    public async Task HandleCallback_ValidConfiguredCallback_StoresTokensAndNavigatesOnce()
    {
        var tokens = new AuthenticationTokensResponse(
            "access-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.VerifyWhatsAppChallengeAsync(
                "verified-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions());
        var callback = new Uri("southbaysoccer://auth/whatsapp?token=verified-token");

        var firstResult = await coordinator.HandleCallbackAsync(callback);
        var secondResult = await coordinator.HandleCallbackAsync(callback);

        firstResult.Should().BeTrue();
        secondResult.Should().BeTrue();
        tokenStore.Verify(store => store.StoreAsync(tokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleCallback_UnapprovedCallback_DoesNotExchangeOrNavigate()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions());

        var result = await coordinator.HandleCallbackAsync(
            new Uri("https://example.com/auth/whatsapp?token=forged"));

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }
}
