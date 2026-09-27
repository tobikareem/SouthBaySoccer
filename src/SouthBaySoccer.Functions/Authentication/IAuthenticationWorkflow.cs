using SouthBaySoccer.Contracts.Authentication;

namespace SouthBaySoccer.Functions.Authentication;

/// <summary>Maps the sign-in, refresh, and sign-out contracts onto Application commands.</summary>
public interface IAuthenticationWorkflow
{
    Task<PhoneSignInStartResponse> BeginPhoneSignInAsync(
        SignInByPhoneRequest request,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> CompleteWhatsAppLoginAsync(
        CompleteWhatsAppLoginRequest request,
        CancellationToken cancellationToken);

    Task<AuthenticationTokensResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken);

    Task SignOutAsync(SignOutCommand command, CancellationToken cancellationToken);
}
