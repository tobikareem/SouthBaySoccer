using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class WhatsAppAuthenticationProviderTests
{
    private static readonly DateTime Now = new(2026, 6, 26, 21, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;

    public WhatsAppAuthenticationProviderTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task CreateChallengeAsync_WhenChallengeRequested_PersistsOnlyHashedSecretsAndSendsOpaqueToken()
    {
        const string phoneNumber = "+13105550123";
        const string maskedPhoneNumber = "+1******0123";
        const string callbackUri = "southbaysoccer://auth/whatsapp-callback";
        var rawToken = $"challenge-{Guid.NewGuid():N}";
        await using var db = database.CreateDbContext();
        var tokenGenerator = new Mock<IWhatsAppChallengeTokenGenerator>();
        tokenGenerator.Setup(x => x.CreateToken()).Returns(rawToken);
        var deliverySender = new Mock<IWhatsAppChallengeDeliverySender>();
        var service = CreateChallengeService(db, tokenGenerator.Object, deliverySender.Object);

        var result = await service.CreateChallengeAsync(
            new WhatsAppChallengeIssueRequest(phoneNumber, maskedPhoneNumber, callbackUri));

        result.ChallengeId.Should().NotBeNullOrWhiteSpace();
        result.MaskedPhoneNumber.Should().Be(maskedPhoneNumber);
        result.ExpiresAtUtc.Should().Be(Now.AddMinutes(10));

        var persisted = await db.WhatsAppSignInChallenges.SingleAsync(x => x.ChallengeId == result.ChallengeId);
        persisted.ChallengeTokenHash.Should().Be(Sha256(rawToken));
        persisted.ChallengeTokenHash.Should().NotBe(rawToken);
        persisted.PhoneNumberHash.Should().Be(Sha256(phoneNumber));
        persisted.PhoneNumberHash.Should().NotBe(phoneNumber);
        persisted.CallbackUriHash.Should().Be(Sha256(callbackUri));
        persisted.CallbackUriHash.Should().NotBe(callbackUri);
        persisted.CreatedBy.Should().Be("system");
        deliverySender.Verify(
            x => x.SendAsync(
                It.Is<WhatsAppChallengeDeliveryRequest>(request =>
                    request.PhoneNumber == phoneNumber &&
                    request.MaskedPhoneNumber == maskedPhoneNumber &&
                    request.ChallengeToken == rawToken &&
                    request.CallbackUri == callbackUri &&
                    request.ExpiresAtUtc == Now.AddMinutes(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyChallengeAsync_WhenChallengeIsValid_ConsumesChallengeAndReturnsPhoneHash()
    {
        const string phoneNumber = "+13105550124";
        const string maskedPhoneNumber = "+1******0124";
        const string callbackUri = "southbaysoccer://auth/whatsapp-callback";
        var rawToken = $"challenge-{Guid.NewGuid():N}";
        await using var db = database.CreateDbContext();
        var tokenGenerator = new Mock<IWhatsAppChallengeTokenGenerator>();
        tokenGenerator.Setup(x => x.CreateToken()).Returns(rawToken);
        var service = CreateChallengeService(db, tokenGenerator.Object);
        var issued = await service.CreateChallengeAsync(
            new WhatsAppChallengeIssueRequest(phoneNumber, maskedPhoneNumber, callbackUri));

        var result = await service.VerifyChallengeAsync(
            new WhatsAppChallengeVerificationRequest(rawToken, callbackUri));

        result.PhoneNumberHash.Should().Be(Sha256(phoneNumber));
        result.MaskedPhoneNumber.Should().Be(maskedPhoneNumber);

        var persisted = await db.WhatsAppSignInChallenges.SingleAsync(x => x.ChallengeId == issued.ChallengeId);
        persisted.ConsumedAtUtc.Should().Be(Now);
        persisted.UpdatedBy.Should().Be("system");

        var reused = () => service.VerifyChallengeAsync(new WhatsAppChallengeVerificationRequest(rawToken, callbackUri));
        await reused.Should().ThrowAsync<InvalidOperationException>();
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

    private static WhatsAppChallengeService CreateChallengeService(
        SouthBaySoccerDbContext db,
        IWhatsAppChallengeTokenGenerator tokenGenerator,
        IWhatsAppChallengeDeliverySender? deliverySender = null)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);

        return new WhatsAppChallengeService(
            db,
            clock.Object,
            tokenGenerator,
            deliverySender ?? Mock.Of<IWhatsAppChallengeDeliverySender>(),
            Options.Create(new WhatsAppChallengeOptions()));
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }
}