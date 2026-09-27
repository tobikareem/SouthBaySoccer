using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// EF-backed refresh-token revocation. Only the token hash is ever looked up; revocation stamps the
/// immutable history rows rather than deleting them.
/// </summary>
public sealed class RefreshTokenRevocationService(
    SouthBaySoccerDbContext dbContext,
    IClock clock,
    IRefreshTokenHasher hasher) : IRefreshTokenRevocationService
{
    /// <inheritdoc />
    public async Task RevokeFamilyAsync(
        Guid identityUserId,
        string refreshToken,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = hasher.Hash(refreshToken);
        var presented = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.IdentityUserId == identityUserId, cancellationToken);
        if (presented is null)
        {
            // A token that is not the caller's (or does not exist) is ignored rather than reported,
            // so sign-out never confirms whether a token value is real.
            return;
        }

        var now = clock.UtcNow;
        var familyTokens = await dbContext.RefreshTokens
            .Where(x => x.IdentityUserId == identityUserId && x.FamilyId == presented.FamilyId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in familyTokens)
        {
            token.RevokedAtUtc = now;
            token.RevocationReason = reason;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAllAsync(Guid identityUserId, string reason, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var activeTokens = await dbContext.RefreshTokens
            .Where(x => x.IdentityUserId == identityUserId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = now;
            token.RevocationReason = reason;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
