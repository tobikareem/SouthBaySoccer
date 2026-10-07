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

        var activity = new UserActivity
        {
            Id = Guid.NewGuid(),
            IdentityUserId = subject.IdentityUserId,
            PlayerProfileId = subject.PlayerProfileId,
            ActivityType = subject.ActivityType,
            OccurredAtUtc = now,
            SessionFamilyId = refreshToken.FamilyId,
            CreatedAt = now,
            CreatedBy = subject.IdentityUserId.ToString("D"),
        };
        dbContext.RefreshTokens.Add(refreshToken);
        dbContext.UserActivities.Add(activity);
        try
        {
            // EF saves both rows atomically; retries retain their ids and unique session-family key.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // A later save on this scoped context must not persist a failed issuance attempt.
            dbContext.Entry(refreshToken).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            dbContext.Entry(activity).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            throw;
        }

        return new AuthenticationTokenSet(
            accessToken.Token,
            refreshTokenSecret,
            accessToken.ExpiresAtUtc);
    }
}
