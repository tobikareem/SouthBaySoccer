using System.Data;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// EF-backed refresh-token exchange service.
/// </summary>
public sealed class RefreshTokenExchangeService : IRefreshTokenExchangeService
{
    private const string ReuseDetectedReason = "RefreshTokenReuseDetected";
    private readonly SouthBaySoccerDbContext dbContext;
    private readonly IClock clock;
    private readonly IRefreshTokenHasher hasher;
    private readonly IRefreshTokenSecretGenerator tokenGenerator;
    private readonly TimeSpan tokenLifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTokenExchangeService"/> class.
    /// </summary>
    /// <param name="dbContext">The persistence context.</param>
    /// <param name="clock">The UTC clock.</param>
    /// <param name="hasher">The refresh-token hasher.</param>
    /// <param name="tokenGenerator">The refresh-token secret generator.</param>
    /// <param name="tokenLifetime">The replacement refresh-token lifetime.</param>
    public RefreshTokenExchangeService(
        SouthBaySoccerDbContext dbContext,
        IClock clock,
        IRefreshTokenHasher hasher,
        IRefreshTokenSecretGenerator tokenGenerator,
        TimeSpan? tokenLifetime = null)
    {
        this.dbContext = dbContext;
        this.clock = clock;
        this.hasher = hasher;
        this.tokenGenerator = tokenGenerator;
        this.tokenLifetime = tokenLifetime ?? TimeSpan.FromDays(30);
    }

    /// <inheritdoc />
    public async Task<RefreshTokenExchangeResult> RotateAsync(
        RefreshTokenExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Invalid);
        }

        var tokenHash = hasher.Hash(request.RefreshToken);
        var now = clock.UtcNow;

        // The exchange must be a single serializable unit: classify the presented token, mark reuse
        // or consumption, create the replacement, and persist the family state together.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var currentToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (currentToken is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Invalid);
        }

        if (currentToken.ConsumedAtUtc is not null)
        {
            await RevokeFamilyForReuseAsync(currentToken, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Reused);
        }

        if (currentToken.RevokedAtUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Revoked);
        }

        if (currentToken.ExpiresAtUtc <= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RefreshTokenExchangeResult(RefreshTokenExchangeStatus.Expired);
        }

        var replacementSecret = tokenGenerator.CreateToken();
        var replacementToken = CreateReplacementToken(currentToken, request, replacementSecret, now);

        currentToken.ConsumedAtUtc = now;
        currentToken.ReplacedByRefreshTokenId = replacementToken.Id;

        dbContext.RefreshTokens.Add(replacementToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenExchangeResult(
            RefreshTokenExchangeStatus.Rotated,
            currentToken.IdentityUserId,
            currentToken.PlayerProfileId,
            replacementSecret,
            replacementToken.Id,
            replacementToken.ExpiresAtUtc);
    }

    private RefreshToken CreateReplacementToken(
        RefreshToken currentToken,
        RefreshTokenExchangeRequest request,
        string replacementSecret,
        DateTime now)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            IdentityUserId = currentToken.IdentityUserId,
            PlayerProfileId = currentToken.PlayerProfileId,
            TokenHash = hasher.Hash(replacementSecret),
            FamilyId = currentToken.FamilyId,
            DeviceId = request.DeviceId ?? currentToken.DeviceId,
            UserAgentHash = HashOptional(request.UserAgent) ?? currentToken.UserAgentHash,
            IpAddressHash = HashOptional(request.IpAddress) ?? currentToken.IpAddressHash,
            ExpiresAtUtc = now.Add(tokenLifetime),
            CreatedAt = now,
            CreatedBy = currentToken.IdentityUserId.ToString(),
        };
    }

    private async Task RevokeFamilyForReuseAsync(
        RefreshToken reusedToken,
        DateTime now,
        CancellationToken cancellationToken)
    {
        reusedToken.ReuseDetectedAtUtc ??= now;
        reusedToken.RevokedAtUtc ??= now;
        reusedToken.RevocationReason ??= ReuseDetectedReason;

        var familyTokens = await dbContext.RefreshTokens
            .Where(x => x.IdentityUserId == reusedToken.IdentityUserId &&
                x.FamilyId == reusedToken.FamilyId &&
                x.Id != reusedToken.Id)
            .ToListAsync(cancellationToken);

        foreach (var familyToken in familyTokens)
        {
            familyToken.RevokedAtUtc ??= now;
            familyToken.RevocationReason ??= ReuseDetectedReason;
            familyToken.RevokedByRefreshTokenId ??= reusedToken.Id;
        }
    }

    private string? HashOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : hasher.Hash(value);
}

