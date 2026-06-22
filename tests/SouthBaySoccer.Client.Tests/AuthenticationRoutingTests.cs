using FluentAssertions;
using Moq;
using SouthBaySoccer.Configuration;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.Client.Tests;

// NAV-1 startup-routing decision: AppStartupService and AuthenticationCoordinator own whether the
// app enters the authenticated Shell. These mock the I/O seams so the decision is verified without a
// MAUI host (the navigator's own window swap is platform-owned and tested via its source contract).
public class AppStartupRoutingTests
{
    private static AuthenticationTokensResponse Tokens() =>
        new("access-token", "refresh-token", DateTime.UtcNow.AddMinutes(15));

    [Fact]
    public async Task TryRestoreSession_WithNoStoredRefreshToken_StaysOnWelcomeBack()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync()).ReturnsAsync((string?)null);
        var authClient = new Mock<IAuthenticationClient>();
        var navigator = new Mock<IAuthenticationNavigator>();

        var service = new AppStartupService(tokenStore.Object, authClient.Object, navigator.Object);

        await service.TryRestoreSessionAsync();

        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Never);
        authClient.Verify(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenStore.Verify(s => s.ClearAsync(), Times.Never);
    }

    [Fact]
    public async Task TryRestoreSession_WithValidRefreshToken_StoresTokensAndEntersAuthenticatedShell()
    {
        var tokens = Tokens();
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync()).ReturnsAsync("stored-refresh");
        var authClient = new Mock<IAuthenticationClient>();
        authClient
            .Setup(c => c.RefreshAsync("stored-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var navigator = new Mock<IAuthenticationNavigator>();
        navigator
            .Setup(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AppStartupService(tokenStore.Object, authClient.Object, navigator.Object);

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(s => s.StoreAsync(tokens), Times.Once);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Once);
        tokenStore.Verify(s => s.ClearAsync(), Times.Never);
    }

    [Fact]
    public async Task TryRestoreSession_WhenRefreshFails_ClearsTokensAndStaysOnWelcomeBack()
    {
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync()).ReturnsAsync("stored-refresh");
        var authClient = new Mock<IAuthenticationClient>();
        authClient
            .Setup(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("refresh rejected"));
        var navigator = new Mock<IAuthenticationNavigator>();

        var service = new AppStartupService(tokenStore.Object, authClient.Object, navigator.Object);

        await service.TryRestoreSessionAsync();

        tokenStore.Verify(s => s.ClearAsync(), Times.Once);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryRestoreSession_WhenCancelled_PropagatesCancellationWithoutClearingTokens()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tokenStore = new Mock<ISecureTokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync()).ReturnsAsync("stored-refresh");
        var authClient = new Mock<IAuthenticationClient>();
        authClient
            .Setup(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var navigator = new Mock<IAuthenticationNavigator>();

        var service = new AppStartupService(tokenStore.Object, authClient.Object, navigator.Object);

        var act = () => service.TryRestoreSessionAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        tokenStore.Verify(s => s.ClearAsync(), Times.Never);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class AuthenticationCoordinatorRoutingTests
{
    private static AuthenticationTokensResponse Tokens() =>
        new("access-token", "refresh-token", DateTime.UtcNow.AddMinutes(15));

    private static AuthenticationCoordinator CreateSeedCoordinator(
        Mock<IAuthenticationClient> authClient,
        Mock<ISecureTokenStore> tokenStore,
        Mock<IAuthenticationNavigator> navigator) =>
        new(
            authClient.Object,
            tokenStore.Object,
            navigator.Object,
            new PickupPalOptions(),
            new ClientDataSourceOptions { DataSource = ClientDataSource.Seed });

    [Fact]
    public async Task TryCompleteChallenge_WithSeedSourceAndValidToken_StoresTokensAndEntersShell()
    {
        var tokens = Tokens();
        var authClient = new Mock<IAuthenticationClient>();
        authClient
            .Setup(c => c.VerifyWhatsAppChallengeAsync("challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        navigator
            .Setup(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coordinator = CreateSeedCoordinator(authClient, tokenStore, navigator);

        var result = await coordinator.TryCompleteChallengeAsync("challenge");

        result.Should().BeTrue();
        tokenStore.Verify(s => s.StoreAsync(tokens), Times.Once);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryCompleteChallenge_CalledTwice_VerifiesOnceAndRemainsIdempotent()
    {
        var authClient = new Mock<IAuthenticationClient>();
        authClient
            .Setup(c => c.VerifyWhatsAppChallengeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tokens());
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();
        navigator
            .Setup(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var coordinator = CreateSeedCoordinator(authClient, tokenStore, navigator);

        (await coordinator.TryCompleteChallengeAsync("challenge")).Should().BeTrue();
        (await coordinator.TryCompleteChallengeAsync("challenge")).Should().BeTrue();

        authClient.Verify(
            c => c.VerifyWhatsAppChallengeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryCompleteChallenge_WithEmptyToken_DoesNotEnterShell()
    {
        var authClient = new Mock<IAuthenticationClient>();
        var tokenStore = new Mock<ISecureTokenStore>();
        var navigator = new Mock<IAuthenticationNavigator>();

        var coordinator = CreateSeedCoordinator(authClient, tokenStore, navigator);

        var result = await coordinator.TryCompleteChallengeAsync("   ");

        result.Should().BeFalse();
        authClient.Verify(
            c => c.VerifyWhatsAppChallengeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        navigator.Verify(n => n.ShowAuthenticatedAppAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
