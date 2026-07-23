using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Authentication;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class PickupPalUserSyncServiceTests
{
    private readonly InfrastructureDatabaseFixture database;

    public PickupPalUserSyncServiceTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task SyncAsync_NewPickupPalUser_CreatesIdentityProfileAndPersistsEmail()
    {
        using var provider = CreateServiceProvider();
        var service = provider.GetRequiredService<IPickupPalUserSyncService>();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationIdentityUser>>();
        var pickupPalUser = CreatePickupPalUser("pickuppal-user-create", "vic@example.test");

        var subject = await service.SyncAsync(pickupPalUser);

        var profile = await db.PlayerProfiles.FindAsync(subject.PlayerProfileId);
        profile.Should().NotBeNull();
        profile!.PickupPalUserId.Should().Be("pickuppal-user-create");
        profile.DisplayName.Should().Be("Vic A");
        profile.Role.Should().Be(PlayerRole.Player);
        profile.PreferredPosition.Should().Be("st, rw, cm");
        profile.PhoneNumberHash.Should().NotBeNullOrWhiteSpace();
        profile.MaskedPhoneNumber.Should().Be("+******9421");

        var identity = await userManager.FindByIdAsync(subject.IdentityUserId.ToString("D"));
        identity.Should().NotBeNull();
        identity!.UserName.Should().Be("pickuppal:pickuppal-user-create");
        identity.Email.Should().Be("vic@example.test");
        identity.NormalizedEmail.Should().Be("VIC@EXAMPLE.TEST");
        identity.PlayerProfileId.Should().Be(profile.Id);
    }

    [Fact]
    public async Task SyncAsync_ExistingPickupPalUser_UpdatesEmailPreferredPositionAndPreservesRoleWithoutDuplicateProfile()
    {
        using var provider = CreateServiceProvider();
        var service = provider.GetRequiredService<IPickupPalUserSyncService>();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();

        var firstSubject = await service.SyncAsync(CreatePickupPalUser("pickuppal-user-update", "old@example.test"));
        var profile = await db.PlayerProfiles.FindAsync(firstSubject.PlayerProfileId);
        profile!.Role = PlayerRole.Admin;
        await db.SaveChangesAsync();

        var secondSubject = await service.SyncAsync(CreatePickupPalUser("pickuppal-user-update", "new@example.test"));

        secondSubject.PlayerProfileId.Should().Be(firstSubject.PlayerProfileId);
        secondSubject.IdentityUserId.Should().Be(firstSubject.IdentityUserId);
        db.PlayerProfiles.Count(x => x.PickupPalUserId == "pickuppal-user-update").Should().Be(1);
        var updatedProfile = await db.PlayerProfiles.FindAsync(firstSubject.PlayerProfileId);
        updatedProfile!.Role.Should().Be(PlayerRole.Admin);
        updatedProfile.PreferredPosition.Should().Be("st, rw, cm");
        secondSubject.Roles.Should().ContainSingle().Which.Should().Be(PlayerRole.Admin.ToString());
        var identity = await provider.GetRequiredService<UserManager<ApplicationIdentityUser>>()
            .FindByIdAsync(firstSubject.IdentityUserId.ToString("D"));
        identity!.Email.Should().Be("new@example.test");
    }

    [Fact]
    public async Task SyncAsync_ConfiguredAdminPhone_PromotesProfileAndTokenSubjectToGameAdmin()
    {
        using var provider = CreateServiceProvider("15106949421");
        var service = provider.GetRequiredService<IPickupPalUserSyncService>();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();

        var subject = await service.SyncAsync(CreatePickupPalUser("pickuppal-user-admin", "admin@example.test"));

        var profile = await db.PlayerProfiles.FindAsync(subject.PlayerProfileId);
        profile!.Role.Should().Be(PlayerRole.GameAdmin);
        subject.Roles.Should().ContainSingle().Which.Should().Be(PlayerRole.GameAdmin.ToString());
    }

    [Fact]
    public async Task SyncAsync_WhenUnclaimedImportedProfileMatchesByPhoneHash_ClaimsItAndPromotesToPlayer()
    {
        using var provider = CreateServiceProvider();
        var service = provider.GetRequiredService<IPickupPalUserSyncService>();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();

        // A prior games import created an unclaimed guest profile keyed only by phone hash. The
        // hash is computed the same way the sync service does: "+" + digits, then SHA-256.
        var importedProfile = new PlayerProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Imported Vic",
            NormalizedDisplayName = "IMPORTED VIC",
            PreferredPosition = string.Empty,
            Role = PlayerRole.Guest,
            IsGuest = true,
            PhoneNumberHash = Sha256Hex("+15106949421"),
            MaskedPhoneNumber = "+******9421",
        };
        db.PlayerProfiles.Add(importedProfile);
        await db.SaveChangesAsync();

        var subject = await service.SyncAsync(CreatePickupPalUser("pickuppal-user-claim", "claim@example.test"));

        subject.PlayerProfileId.Should().Be(
            importedProfile.Id, "first sign-in claims the imported profile instead of creating a duplicate");
        db.PlayerProfiles.Count(x => x.PhoneNumberHash == importedProfile.PhoneNumberHash).Should().Be(1);
        var claimed = await db.PlayerProfiles.FindAsync(importedProfile.Id);
        claimed!.PickupPalUserId.Should().Be("pickuppal-user-claim");
        claimed.IdentityUserId.Should().NotBeNull();
        claimed.Role.Should().Be(PlayerRole.Player, "an unclaimed guest is promoted to Player on sign-in");
        claimed.IsGuest.Should().BeFalse();
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private ServiceProvider CreateServiceProvider(string adminPhoneNumbers = "")
    {
        var services = new ServiceCollection();
        services.Configure<AdminPhoneNumberOptions>(options => options.AdminPhoneNumbers = adminPhoneNumbers);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static PickupPalUser CreatePickupPalUser(string id, string email) =>
        new(
            id,
            email,
            "15106949421",
            "Vic",
            "A",
            null,
            null,
            new[] { "st", "rw", "cm" },
            DateTime.UtcNow);
}


