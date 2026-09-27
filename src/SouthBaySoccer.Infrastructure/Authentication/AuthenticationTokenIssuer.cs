using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Issues SouthBaySoccer access and refresh tokens after trusted authentication.
/// </summary>
public sealed class AuthenticationTokenIssuer(
    SouthBaySoccerDbContext dbContext,
    IClock clock,
    ITokenService tokenService,
    IRefreshTokenHasher refreshTokenHasher,
    IRefreshTokenSecretGenerator refreshTokenSecretGenerator) : IAuthenticationTokenIssuer
{
    /// <summary>Refresh-token lifetime used when the caller does not ask for a specific one.</summary>
    public static readonly TimeSpan DefaultRefreshTokenLifetime = TimeSpan.FromDays(30);

    public Task<AuthenticationTokenSet> IssueTokensAsync(
        AuthenticationTokenSubject subject,
        CancellationToken cancellationToken = default) =>
        IssueTokensAsync(subject, DefaultRefreshTokenLifetime, cancellationToken);

    public async Task<AuthenticationTokenSet> IssueTokensAsync(
        AuthenticationTokenSubject subject,
        TimeSpan refreshTokenLifetime,
        CancellationToken cancellationToken = default)
    {
        var policies = AuthenticationPolicyMapper.FromRoles(subject.Roles);
        var accessToken = tokenService.IssueAccessToken(
            new AccessTokenIssueRequest(subject.IdentityUserId, subject.Roles, policies));
        var refreshTokenSecret = refreshTokenSecretGenerator.CreateToken();
        var now = clock.UtcNow;
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            IdentityUserId = subject.IdentityUserId,
            PlayerProfileId = subject.PlayerProfileId,
            TokenHash = refreshTokenHasher.Hash(refreshTokenSecret),
            FamilyId = Guid.NewGuid(),
            ExpiresAtUtc = now.Add(refreshTokenLifetime),
            CreatedAt = now,
            CreatedBy = subject.IdentityUserId.ToString("D"),
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthenticationTokenSet(
            accessToken.Token,
            refreshTokenSecret,
            accessToken.ExpiresAtUtc);
    }
}
