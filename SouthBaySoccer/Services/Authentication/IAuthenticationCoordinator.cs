namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationCoordinator
{
    Task<bool> TryCompleteChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default);

    Task<bool> HandleCallbackAsync(Uri callbackUri, CancellationToken cancellationToken = default);
}
