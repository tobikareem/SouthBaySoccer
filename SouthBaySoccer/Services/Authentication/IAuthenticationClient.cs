using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Services.Authentication;

public interface IAuthenticationClient
{
    Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(
        string phoneNumber,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
