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
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });
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
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var result = await coordinator.HandleCallbackAsync(
            new Uri("https://example.com/auth/whatsapp?token=forged"));

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleCallback_ConfiguredSchemeWithoutVerifiedToken_DoesNotAuthenticate()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        // Merely returning to the app on the approved scheme (no verified one-time token)
        // must never establish a session (AUTH-8/AUTH-9, INV-11 fail-closed).
        var result = await coordinator.HandleCallbackAsync(
            new Uri("southbaysoccer://auth/whatsapp"));

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryCompleteChallenge_SeedProvider_StoresTokensAndNavigates()
    {
        var tokens = new AuthenticationTokensResponse(
            "seed-access-token",
            "seed-refresh-token",
            DateTime.UtcNow.AddMinutes(15));
        var authenticationClient = new Mock<IAuthenticationClient>();
        authenticationClient
            .Setup(client => client.VerifyWhatsAppChallengeAsync(
                "seed-whatsapp-challenge",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Seed });

        var result = await coordinator.TryCompleteChallengeAsync("seed-whatsapp-challenge");

        result.Should().BeTrue();
        tokenStore.Verify(store => store.StoreAsync(tokens), Times.Once);
        navigator.Verify(item => item.ShowAuthenticatedAppAsync(), Times.Once);
    }

    [Fact]
    public async Task TryCompleteChallenge_ApiProvider_DoesNotBypassVerifiedCallback()
    {
        var authenticationClient = new Mock<IAuthenticationClient>(MockBehavior.Strict);
        var tokenStore = new Mock<ISecureTokenStore>(MockBehavior.Strict);
        var navigator = new Mock<IAuthenticationNavigator>(MockBehavior.Strict);
        var coordinator = new AuthenticationCoordinator(
            authenticationClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Api });

        var result = await coordinator.TryCompleteChallengeAsync("unverified-challenge-id");

        result.Should().BeFalse();
        authenticationClient.VerifyNoOtherCalls();
        tokenStore.VerifyNoOtherCalls();
        navigator.VerifyNoOtherCalls();
    }
}
