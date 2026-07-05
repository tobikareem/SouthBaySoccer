using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationCoordinator
{
    Task CompleteSignInAsync(
        AuthenticationTokensResponse tokens,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken = default);

    Task<bool> HandleCallbackAsync(Uri callbackUri, CancellationToken cancellationToken = default);
}
