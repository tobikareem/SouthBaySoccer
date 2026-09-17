using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>Runs against SQL Server LocalDB (Windows only, like every test in this collection).</summary>
[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class RefreshTokenRevocationServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;
    private readonly RefreshTokenHasher hasher = new();

    public RefreshTokenRevocationServiceTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task RevokeFamilyAsync_WhenTokenBelongsToUser_RevokesEveryActiveTokenInItsFamilyOnly()
    {
        var identityUserId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var rawToken = $"raw-{Guid.NewGuid():N}";
        var presented = CreateToken(rawToken, identityUserId, familyId);
        var sibling = CreateToken($"sibling-{Guid.NewGuid():N}", identityUserId, familyId);
        var otherFamily = CreateToken($"other-{Guid.NewGuid():N}", identityUserId, Guid.NewGuid());
        await SeedAsync(presented, sibling, otherFamily);
        await using var db = database.CreateDbContext();
        var service = CreateService(db);

        await service.RevokeFamilyAsync(identityUserId, rawToken, "SignOut");

        await using var assertionDb = database.CreateDbContext();
        (await assertionDb.RefreshTokens.SingleAsync(x => x.Id == presented.Id)).RevocationReason.Should().Be("SignOut");
        (await assertionDb.RefreshTokens.SingleAsync(x => x.Id == sibling.Id)).RevokedAtUtc.Should().Be(Now);
        (await assertionDb.RefreshTokens.SingleAsync(x => x.Id == otherFamily.Id)).RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RevokeFamilyAsync_WhenTokenBelongsToAnotherUser_RevokesNothing()
    {
        var rawToken = $"raw-{Guid.NewGuid():N}";
        var victim = CreateToken(rawToken, Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(victim);
        await using var db = database.CreateDbContext();
        var service = CreateService(db);

        await service.RevokeFamilyAsync(Guid.NewGuid(), rawToken, "SignOut");

        await using var assertionDb = database.CreateDbContext();
        (await assertionDb.RefreshTokens.SingleAsync(x => x.Id == victim.Id)).RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAllAsync_WhenUserHasSeveralFamilies_RevokesEveryActiveToken()
    {
        var identityUserId = Guid.NewGuid();
        var first = CreateToken($"a-{Guid.NewGuid():N}", identityUserId, Guid.NewGuid());
        var second = CreateToken($"b-{Guid.NewGuid():N}", identityUserId, Guid.NewGuid());
        var alreadyRevoked = CreateToken($"c-{Guid.NewGuid():N}", identityUserId, Guid.NewGuid());
        alreadyRevoked.RevokedAtUtc = Now.AddDays(-1);
        alreadyRevoked.RevocationReason = "Earlier";
        await SeedAsync(first, second, alreadyRevoked);
        await using var db = database.CreateDbContext();
        var service = CreateService(db);

        await service.RevokeAllAsync(identityUserId, "AccountDeleted");

        await using var assertionDb = database.CreateDbContext();
        var tokens = await assertionDb.RefreshTokens.Where(x => x.IdentityUserId == identityUserId).ToListAsync();
        tokens.Should().HaveCount(3).And.OnlyContain(token => token.RevokedAtUtc != null);
        tokens.Single(x => x.Id == alreadyRevoked.Id).RevocationReason.Should().Be("Earlier");
    }

    private RefreshTokenRevocationService CreateService(Persistence.SouthBaySoccerDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        return new RefreshTokenRevocationService(db, clock.Object, hasher);
    }

    private RefreshToken CreateToken(string rawToken, Guid identityUserId, Guid familyId) => new()
    {
        Id = Guid.NewGuid(),
        IdentityUserId = identityUserId,
        TokenHash = hasher.Hash(rawToken),
        FamilyId = familyId,
        ExpiresAtUtc = Now.AddDays(7),
        CreatedAt = Now.AddDays(-1),
    };

    private async Task SeedAsync(params RefreshToken[] tokens)
    {
        await using var db = database.CreateDbContext();
        db.RefreshTokens.AddRange(tokens);
        await db.SaveChangesAsync();
    }
}
