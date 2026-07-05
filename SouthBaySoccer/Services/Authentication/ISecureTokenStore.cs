using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public interface ISecureTokenStore
{
    Task StoreAsync(AuthenticationTokensResponse tokens);
    Task<string?> GetAccessTokenAsync();
    Task<DateTime?> GetAccessTokenExpiresAtUtcAsync();
    Task<string?> GetRefreshTokenAsync();
    Task ClearAsync();
}

