using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Functions.Authentication;

public sealed class AuthenticationWorkflow(
    BeginPhoneSignInCommandHandler beginPhoneSignInHandler,
    CompleteWhatsAppLoginCommandHandler completeWhatsAppLoginHandler,
    IRefreshTokenExchangeService refreshTokenExchangeService,
    IRefreshTokenRevocationService refreshTokenRevocationService,
    IWhatsAppIdentityResolver identityResolver,
    ITokenService tokenService) : IAuthenticationWorkflow
{
    private const string SignOutRevocationReason = "SignOut";

    public async Task<PhoneSignInStartResponse> BeginPhoneSignInAsync(
        SignInByPhoneRequest request,
        CancellationToken cancellationToken)
    {
        var result = await beginPhoneSignInHandler.HandleAsync(
            new BeginPhoneSignInCommand(request.PhoneNumber),
            cancellationToken);

        return new PhoneSignInStartResponse(
            result.VerificationRequired,
            result.PhoneMasked,
            result.DisplayName,
            result.Tokens is null ? null : ToResponse(result.Tokens));
    }

    public async Task<AuthenticationTokensResponse> CompleteWhatsAppLoginAsync(
        CompleteWhatsAppLoginRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await completeWhatsAppLoginHandler.HandleAsync(
            new CompleteWhatsAppLoginCommand(request.Token, request.RememberDevice),
            cancellationToken);

        return ToResponse(tokens);
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

    // Revoking the presented token's family is what makes "sign out" end a remembered device: the
    // next launch finds no valid refresh token and goes through WhatsApp verification again.
    public Task SignOutAsync(SignOutCommand command, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(command.RefreshToken)
            ? Task.CompletedTask
            : refreshTokenRevocationService.RevokeFamilyAsync(
                command.UserId,
                command.RefreshToken,
                SignOutRevocationReason,
                cancellationToken);

    private static AuthenticationTokensResponse ToResponse(AuthenticationTokenSet tokens) =>
        new(tokens.AccessToken, tokens.RefreshToken, tokens.AccessTokenExpiresAtUtc);
}
