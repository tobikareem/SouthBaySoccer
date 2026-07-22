using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class RefreshTokenExchangeServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 26, 20, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;
    private readonly RefreshTokenHasher hasher = new();

    public RefreshTokenExchangeServiceTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task RotateAsync_WhenTokenIsActive_ConsumesCurrentTokenAndPersistsHashedReplacement()
    {
        var rawToken = $"raw-{Guid.NewGuid():N}";
        var replacementSecret = $"replacement-{Guid.NewGuid():N}";
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var currentToken = CreateRefreshToken(rawToken, identityUserId, playerProfileId, familyId);
        await SeedAsync(currentToken);
        using var db = database.CreateDbContext();
        var tokenGenerator = CreateTokenGenerator(replacementSecret);
        var service = CreateService(db, tokenGenerator.Object);

        var result = await service.RotateAsync(new RefreshTokenExchangeRequest(
            rawToken,
            "device-next",
            "SouthBaySoccer/1.0",
            "203.0.113.10"));

        result.Status.Should().Be(RefreshTokenExchangeStatus.Rotated);
        result.Succeeded.Should().BeTrue();
        result.IdentityUserId.Should().Be(identityUserId);
        result.PlayerProfileId.Should().Be(playerProfileId);
        result.RefreshToken.Should().Be(replacementSecret);
        result.RefreshTokenId.Should().NotBeNull();
        result.RefreshTokenExpiresAtUtc.Should().Be(Now.AddDays(30));
        tokenGenerator.Verify(x => x.CreateToken(), Times.Once);

        await using var assertionDb = database.CreateDbContext();
        var consumedToken = await assertionDb.RefreshTokens.SingleAsync(x => x.Id == currentToken.Id);
        var replacementToken = await assertionDb.RefreshTokens.SingleAsync(x => x.Id == result.RefreshTokenId);

        consumedToken.ConsumedAtUtc.Should().Be(Now);
        consumedToken.ReplacedByRefreshTokenId.Should().Be(replacementToken.Id);
        consumedToken.TokenHash.Should().Be(hasher.Hash(rawToken));
        consumedToken.TokenHash.Should().NotBe(rawToken);

        replacementToken.TokenHash.Should().Be(hasher.Hash(replacementSecret));
        replacementToken.TokenHash.Should().NotBe(replacementSecret);
        replacementToken.FamilyId.Should().Be(familyId);
        replacementToken.DeviceId.Should().Be("device-next");
        replacementToken.UserAgentHash.Should().Be(hasher.Hash("SouthBaySoccer/1.0"));
        replacementToken.IpAddressHash.Should().Be(hasher.Hash("203.0.113.10"));
    }

    [Fact]
    public async Task RotateAsync_WhenConsumedTokenIsPresented_MarksReuseAndRevokesTokenFamily()
    {
        var rawToken = $"raw-{Guid.NewGuid():N}";
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var reusedToken = CreateRefreshToken(rawToken, identityUserId, playerProfileId, familyId);
        reusedToken.ConsumedAtUtc = Now.AddMinutes(-5);
        var activeFamilyToken = CreateRefreshToken($"active-{Guid.NewGuid():N}", identityUserId, playerProfileId, familyId);
        await SeedAsync(reusedToken, activeFamilyToken);
        using var db = database.CreateDbContext();
        var tokenGenerator = CreateTokenGenerator($"replacement-{Guid.NewGuid():N}");
        var service = CreateService(db, tokenGenerator.Object);

        var result = await service.RotateAsync(new RefreshTokenExchangeRequest(rawToken));

        result.Status.Should().Be(RefreshTokenExchangeStatus.Reused);
        result.Succeeded.Should().BeFalse();
        result.RefreshToken.Should().BeNull();
        // A candidate replacement is generated before token classification so a retry after an
        // ambiguous commit can recognize its own successful rotation without treating it as reuse.
        tokenGenerator.Verify(x => x.CreateToken(), Times.Once);

        await using var assertionDb = database.CreateDbContext();
        var reused = await assertionDb.RefreshTokens.SingleAsync(x => x.Id == reusedToken.Id);
        var revokedFamilyToken = await assertionDb.RefreshTokens.SingleAsync(x => x.Id == activeFamilyToken.Id);

        reused.ReuseDetectedAtUtc.Should().Be(Now);
        reused.RevokedAtUtc.Should().Be(Now);
        reused.RevocationReason.Should().Be("RefreshTokenReuseDetected");
        reused.RevokedByRefreshTokenId.Should().BeNull();

        revokedFamilyToken.RevokedAtUtc.Should().Be(Now);
        revokedFamilyToken.RevocationReason.Should().Be("RefreshTokenReuseDetected");
        revokedFamilyToken.RevokedByRefreshTokenId.Should().Be(reused.Id);
    }

    [Fact]
    public async Task RotateAsync_WhenTokenIsExpired_ReturnsExpiredWithoutConsumingToken()
    {
        var rawToken = $"raw-{Guid.NewGuid():N}";
        var currentToken = CreateRefreshToken(rawToken, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        currentToken.ExpiresAtUtc = Now.AddSeconds(-1);
        await SeedAsync(currentToken);
        using var db = database.CreateDbContext();
        var tokenGenerator = CreateTokenGenerator($"replacement-{Guid.NewGuid():N}");
        var service = CreateService(db, tokenGenerator.Object);

        var result = await service.RotateAsync(new RefreshTokenExchangeRequest(rawToken));

        result.Status.Should().Be(RefreshTokenExchangeStatus.Expired);
        result.Succeeded.Should().BeFalse();
        // A candidate replacement is generated before token classification so a retry after an
        // ambiguous commit can recognize its own successful rotation without consuming this token.
        tokenGenerator.Verify(x => x.CreateToken(), Times.Once);

        await using var assertionDb = database.CreateDbContext();
        var expiredToken = await assertionDb.RefreshTokens.SingleAsync(x => x.Id == currentToken.Id);
        expiredToken.ConsumedAtUtc.Should().BeNull();
        expiredToken.ReplacedByRefreshTokenId.Should().BeNull();
        expiredToken.RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_WhenRawTokenIsUnknown_ReturnsInvalidWithoutPersistingRawToken()
    {
        using var db = database.CreateDbContext();
        var service = CreateService(db, CreateTokenGenerator($"replacement-{Guid.NewGuid():N}").Object);
        var rawToken = $"missing-{Guid.NewGuid():N}";

        var result = await service.RotateAsync(new RefreshTokenExchangeRequest(rawToken));

        result.Status.Should().Be(RefreshTokenExchangeStatus.Invalid);

        await using var assertionDb = database.CreateDbContext();
        var persistedRawToken = await assertionDb.RefreshTokens.AnyAsync(x => x.TokenHash == rawToken);
        persistedRawToken.Should().BeFalse();
    }

    private RefreshTokenExchangeService CreateService(
        SouthBaySoccerDbContext db,
        IRefreshTokenSecretGenerator tokenGenerator)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        return new RefreshTokenExchangeService(db, clock.Object, hasher, tokenGenerator);
    }

    private static Mock<IRefreshTokenSecretGenerator> CreateTokenGenerator(string replacementSecret)
    {
        var tokenGenerator = new Mock<IRefreshTokenSecretGenerator>();
        tokenGenerator.Setup(x => x.CreateToken()).Returns(replacementSecret);
        return tokenGenerator;
    }

    private RefreshToken CreateRefreshToken(
        string rawToken,
        Guid identityUserId,
        Guid? playerProfileId,
        Guid familyId)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            IdentityUserId = identityUserId,
            PlayerProfileId = playerProfileId,
            TokenHash = hasher.Hash(rawToken),
            FamilyId = familyId,
            DeviceId = $"device-{Guid.NewGuid():N}",
            UserAgentHash = hasher.Hash("seed-user-agent"),
            IpAddressHash = hasher.Hash("198.51.100.20"),
            ExpiresAtUtc = Now.AddDays(7),
            CreatedAt = Now.AddDays(-1),
        };
    }

    private async Task SeedAsync(params RefreshToken[] refreshTokens)
    {
        await using var db = database.CreateDbContext();
        db.RefreshTokens.AddRange(refreshTokens);
        await db.SaveChangesAsync();
    }
}
