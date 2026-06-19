using SouthBaySoccer.Contracts.Authentication;
using SouthBaySoccer.Services.Authentication;

namespace SouthBaySoccer.SeedData;

public sealed class SeedAuthenticationClient : IAuthenticationClient
{
    private static readonly DateTime ChallengeExpiresAtUtc =
        new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime AccessTokenExpiresAtUtc =
        new(2099, 1, 1, 1, 0, 0, DateTimeKind.Utc);

    public Task<RequestWhatsAppChallengeResponse> RequestWhatsAppChallengeAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new RequestWhatsAppChallengeResponse(
                "seed-whatsapp-challenge",
                ChallengeExpiresAtUtc));
    }

    public Task<AuthenticationTokensResponse> VerifyWhatsAppChallengeAsync(
        string challengeToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateTokens());
    }

    public Task<AuthenticationTokensResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateTokens());
    }

    private static AuthenticationTokensResponse CreateTokens() =>
        new(
            "seed-access-token",
            "seed-refresh-token",
            AccessTokenExpiresAtUtc);
}
