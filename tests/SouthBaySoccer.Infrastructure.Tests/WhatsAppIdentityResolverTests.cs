using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Authentication;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class WhatsAppIdentityResolverTests
{
    private static readonly DateTime Now = new(2026, 6, 26, 21, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;

    public WhatsAppIdentityResolverTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task FindByVerifiedPhoneNumberHashAsync_WhenPlayerProfileMatches_ReturnsIdentityProjection()
    {
        const string phoneNumber = "+13105550125";
        var identityUserId = Guid.NewGuid();
        var playerProfileId = Guid.NewGuid();
        await using (var seedDb = database.CreateDbContext())
        {
            seedDb.PlayerProfiles.Add(new PlayerProfile
            {
                Id = playerProfileId,
                IdentityUserId = identityUserId,
                DisplayName = "Tobi",
                NormalizedDisplayName = "TOBI",
                PreferredPosition = "Midfielder",
                PhoneNumberHash = Sha256(phoneNumber),
                MaskedPhoneNumber = "+1******0125",
                Role = PlayerRole.Captain,
                CreatedAt = Now,
            });
            await seedDb.SaveChangesAsync();
        }

        await using var db = database.CreateDbContext();
        var resolver = new WhatsAppIdentityResolver(db);

        var identity = await resolver.FindByVerifiedPhoneNumberHashAsync(Sha256(phoneNumber));

        identity.Should().NotBeNull();
        identity!.IdentityUserId.Should().Be(identityUserId);
        identity.PlayerProfileId.Should().Be(playerProfileId);
        identity.MaskedPhoneNumber.Should().Be("+1******0125");
        identity.Roles.Should().ContainSingle().Which.Should().Be(PlayerRole.Captain.ToString());
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }
}
