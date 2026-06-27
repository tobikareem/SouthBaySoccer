using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Functions.Authentication;

public interface IWhatsAppAuthenticationWorkflow
{
    Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(
        RequestWhatsAppChallengeRequest request,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(
        VerifyWhatsAppChallengeRequest request,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken);

    Task SignOutAsync(SignOutCommand command, CancellationToken cancellationToken);
}
