using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public sealed class SecureTokenStore : ISecureTokenStore
{
    private const string AccessTokenKey = "auth.access_token";
    private const string RefreshTokenKey = "auth.refresh_token";
    private const string AccessTokenExpiryKey = "auth.access_token_expiry";

    public async Task StoreAsync(AuthenticationTokensResponse tokens)
    {
        try
        {
            await SecureStorage.Default.SetAsync(AccessTokenKey, tokens.AccessToken);
            await SecureStorage.Default.SetAsync(RefreshTokenKey, tokens.RefreshToken);
            await SecureStorage.Default.SetAsync(
                AccessTokenExpiryKey,
                tokens.AccessTokenExpiresAtUtc.ToUniversalTime().ToString("O"));
        }
        catch
        {
            await ClearAsync();
            throw;
        }
    }


    public Task<string?> GetAccessTokenAsync() =>
        SecureStorage.Default.GetAsync(AccessTokenKey);

    public async Task<DateTime?> GetAccessTokenExpiresAtUtcAsync()
    {
        var value = await SecureStorage.Default.GetAsync(AccessTokenExpiryKey);
        return DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var expiresAtUtc)
            ? expiresAtUtc
            : null;
    }
    public Task<string?> GetRefreshTokenAsync() =>
        SecureStorage.Default.GetAsync(RefreshTokenKey);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        SecureStorage.Default.Remove(AccessTokenExpiryKey);
        return Task.CompletedTask;
    }
}


