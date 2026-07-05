namespace SouthBaySoccer.Services.Clients;

public interface IAuthenticationSessionRefresher
{
    Task<string?> GetValidAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
