using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>
/// Runs against SQL Server LocalDB (Windows only, like every test in this collection). Exercises the
/// execution-strategy transaction with retry-on-failure enabled, which is the configuration that
/// rejects bare transactions in production.
/// </summary>
[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class LocalAccountDeletionServiceTests
{
    private readonly InfrastructureDatabaseFixture database;

    public LocalAccountDeletionServiceTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task DeleteAsync_WhenLinkedAccountExists_SoftDeletesRecordsAnonymizesIdentityAndRevokesTokens()
    {
        using var provider = CreateServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationIdentityUser>>();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();
        var pickupPalUserId = $"pp-delete-{Guid.NewGuid():N}";
        var email = $"delete-{Guid.NewGuid():N}@example.test";
        var identityUser = new ApplicationIdentityUser
        {
            Id = Guid.NewGuid(),
            UserName = $"pickuppal:{pickupPalUserId}",
            Email = email,
            EmailConfirmed = true,
        };
        (await userManager.CreateAsync(identityUser)).Succeeded.Should().BeTrue();
        var profile = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            IdentityUserId = identityUser.Id,
            PickupPalUserId = pickupPalUserId,
            DisplayName = "Delete Me",
            NormalizedDisplayName = "DELETE ME",
            PreferredPosition = string.Empty,
            Role = PlayerRole.Player,
        };
        db.PlayerProfiles.Add(profile);
        db.EmergencyContacts.Add(new EmergencyContact
        {
            Id = Guid.NewGuid(),
            PlayerProfileId = profile.Id,
            Name = "Contact",
            PhoneNumberHash = new string('A', 64),
            MaskedPhoneNumber = "+******0000",
        });
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            IdentityUserId = identityUser.Id,
            PlayerProfileId = profile.Id,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            FamilyId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync();
        var service = provider.GetRequiredService<ILocalAccountDeletionService>();

        var deletion = await service.DeleteAsync(identityUser.Id);

        deletion.PlayerProfileId.Should().Be(profile.Id);
        deletion.PickupPalUserId.Should().Be(pickupPalUserId);
        await using var assertionDb = database.CreateDbContext();
        (await assertionDb.PlayerProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == profile.Id)).IsDeleted.Should().BeTrue();
        (await assertionDb.EmergencyContacts.IgnoreQueryFilters().SingleAsync(x => x.PlayerProfileId == profile.Id)).IsDeleted.Should().BeTrue();
        (await assertionDb.RefreshTokens.SingleAsync(x => x.IdentityUserId == identityUser.Id)).RevokedAtUtc.Should().NotBeNull();
        var anonymized = await assertionDb.Users.SingleAsync(x => x.Id == identityUser.Id);
        anonymized.Email.Should().NotBe(email).And.EndWith("@deleted.invalid");
        anonymized.UserName.Should().StartWith("deleted:");
        anonymized.LockoutEnd.Should().Be(DateTimeOffset.MaxValue);
        anonymized.PlayerProfileId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenProfileAlreadyDeleted_ReturnsNoPickupPalUserId()
    {
        using var provider = CreateServiceProvider();
        var service = provider.GetRequiredService<ILocalAccountDeletionService>();

        var deletion = await service.DeleteAsync(Guid.NewGuid());

        deletion.PlayerProfileId.Should().BeNull();
        deletion.PickupPalUserId.Should().BeNull();
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }
}
