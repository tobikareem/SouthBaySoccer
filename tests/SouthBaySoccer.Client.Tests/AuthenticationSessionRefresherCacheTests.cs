using FluentAssertions;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// Pins the in-memory access-token copy. Every authenticated request asks for a token, and each
/// miss cost two platform-keystore decrypts, so this is a fixed per-request cost on every screen.
/// </summary>
public sealed class AuthenticationSessionRefresherCacheTests
{
    [Fact]
    public async Task GetValidAccessTokenAsync_WhenCalledRepeatedly_ReadsSecureStorageOnce()
    {
        var store = new CountingSecureTokenStore(
            accessToken: "token-1",
            expiresAtUtc: DateTime.UtcNow.AddMinutes(30));
        var refresher = new AuthenticationSessionRefresher(store, new NeverCalledAuthenticationClient());

        var first = await refresher.GetValidAccessTokenAsync();
        await refresher.GetValidAccessTokenAsync();
        await refresher.GetValidAccessTokenAsync();

        first.Should().Be("token-1");
        store.AccessTokenReads.Should().Be(1, "the warm path must not touch secure storage again");
        store.ExpiryReads.Should().Be(1);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTheCachedTokenHasExpired_DoesNotServeIt()
    {
        var store = new CountingSecureTokenStore(
            accessToken: "stale-token",
            expiresAtUtc: DateTime.UtcNow.AddSeconds(5),
            refreshToken: null);
        var refresher = new AuthenticationSessionRefresher(store, new NeverCalledAuthenticationClient());

        // Inside the one-minute refresh skew, so the token counts as unusable even though it is
        // technically still valid; with no refresh token there is nothing to exchange.
        var token = await refresher.GetValidAccessTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_AfterARefresh_ServesTheNewTokenWithoutRereadingStorage()
    {
        var store = new CountingSecureTokenStore(
            accessToken: null,
            expiresAtUtc: null,
            refreshToken: "refresh-1");
        var authenticationClient = new StubAuthenticationClient(new AuthenticationTokensResponse(
            "token-2",
            "refresh-2",
            DateTime.UtcNow.AddMinutes(30)));
        var refresher = new AuthenticationSessionRefresher(store, authenticationClient);

        var refreshed = await refresher.GetValidAccessTokenAsync();
        var readsAfterRefresh = store.AccessTokenReads;
        var reused = await refresher.GetValidAccessTokenAsync();

        refreshed.Should().Be("token-2");
        reused.Should().Be("token-2");
        store.AccessTokenReads.Should().Be(readsAfterRefresh, "a refresh populates the memory copy directly");
        authenticationClient.RefreshCalls.Should().Be(1);
    }

    private sealed class CountingSecureTokenStore(
        string? accessToken,
        DateTime? expiresAtUtc,
        string? refreshToken = "refresh-token") : ISecureTokenStore
    {
        public int AccessTokenReads { get; private set; }

        public int ExpiryReads { get; private set; }

        public Task StoreAsync(AuthenticationTokensResponse tokens)
        {
            accessToken = tokens.AccessToken;
            expiresAtUtc = tokens.AccessTokenExpiresAtUtc;
            refreshToken = tokens.RefreshToken;
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync()
        {
            AccessTokenReads++;
            return Task.FromResult(accessToken);
        }

        public Task<DateTime?> GetAccessTokenExpiresAtUtcAsync()
        {
            ExpiryReads++;
            return Task.FromResult(expiresAtUtc);
        }

        public Task<string?> GetRefreshTokenAsync() => Task.FromResult(refreshToken);

        public Task ClearAsync()
        {
            accessToken = null;
            expiresAtUtc = null;
            refreshToken = null;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthenticationClient(AuthenticationTokensResponse tokens) : IAuthenticationClient
    {
        public int RefreshCalls { get; private set; }

        public Task<AuthenticationTokensResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(tokens);
        }

        public Task<AuthenticationTokensResponse> SignInByPhoneAsync(string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(string challengeToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NeverCalledAuthenticationClient : IAuthenticationClient
    {
        public Task<AuthenticationTokensResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Refresh must not be attempted on the warm path.");

        public Task<AuthenticationTokensResponse> SignInByPhoneAsync(string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(string phoneNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(string challengeToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
