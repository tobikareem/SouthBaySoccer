using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Functions.Authentication;

public sealed class WhatsAppAuthenticationWorkflow(
    RequestWhatsAppChallengeCommandHandler requestChallengeHandler,
    VerifyWhatsAppChallengeCommandHandler verifyChallengeHandler,
    IRefreshTokenExchangeService refreshTokenExchangeService,
    IWhatsAppIdentityResolver identityResolver,
    ITokenService tokenService) : IWhatsAppAuthenticationWorkflow
{
    public async Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(
        RequestWhatsAppChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await requestChallengeHandler.HandleAsync(
            new RequestWhatsAppChallengeCommand(request.PhoneNumber, request.CallbackUri),
            cancellationToken);

        return new RequestWhatsAppChallengeResponse(result.ChallengeId, result.ExpiresAtUtc);
    }

    public async Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(
        VerifyWhatsAppChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await verifyChallengeHandler.HandleAsync(
            new VerifyWhatsAppChallengeCommand(request.ChallengeToken, request.CallbackUri),
            cancellationToken);

        return ToResponse(result.Tokens);
    }

    public async Task<AuthenticationTokensResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var exchange = await refreshTokenExchangeService.RotateAsync(
            new RefreshTokenExchangeRequest(request.RefreshToken),
            cancellationToken);

        if (!exchange.Succeeded ||
            exchange.IdentityUserId is null ||
            exchange.RefreshToken is null)
        {
            throw new UnauthenticatedException();
        }

        var identity = await identityResolver.FindByIdentityUserIdAsync(
            exchange.IdentityUserId.Value,
            cancellationToken);

        if (identity is null)
        {
            throw new UnauthenticatedException();
        }

        var issuedAccessToken = tokenService.IssueAccessToken(
            new AccessTokenIssueRequest(
                identity.IdentityUserId,
                identity.Roles,
                AuthenticationPolicyMapper.FromRoles(identity.Roles)));

        return new AuthenticationTokensResponse(
            issuedAccessToken.Token,
            exchange.RefreshToken,
            issuedAccessToken.ExpiresAtUtc);
    }

    public Task SignOutAsync(SignOutCommand command, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private static AuthenticationTokensResponse ToResponse(AuthenticationTokenSet tokens) =>
        new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAtUtc);
}
