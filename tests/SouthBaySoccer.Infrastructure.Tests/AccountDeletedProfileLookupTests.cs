using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class AccountDeletedProfileLookupTests(InfrastructureDatabaseFixture database)
{
    [Fact]
    public async Task ListAccountDeletedProfilesAsync_WhenProfilesMatchKeys_ExcludesActiveAndOrdinarySoftDeletedProfiles()
    {
        using var provider = new ServiceCollection().AddInfrastructure(database.ConnectionString).BuildServiceProvider();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();
        var deletedIdentity = new ApplicationIdentityUser
        {
            Id = Guid.NewGuid(), UserName = $"deleted:{Guid.NewGuid():N}", PlayerProfileId = null,
        };
        var ordinaryIdentity = new ApplicationIdentityUser { Id = Guid.NewGuid(), UserName = $"player:{Guid.NewGuid():N}" };
        db.Users.AddRange(deletedIdentity, ordinaryIdentity);
        var marker = Profile(deletedIdentity.Id);
        var ordinary = Profile(ordinaryIdentity.Id);
        var imported = Profile(null);
        var active = Profile(null);
        db.PlayerProfiles.AddRange(marker, ordinary, imported, active);
        await db.SaveChangesAsync();
        // The audit interceptor initializes every inserted entity as active. Model deletion as
        // a subsequent update, just as the real account-deletion and profile-merge flows do.
        marker.IsDeleted = true;
        ordinary.IsDeleted = true;
        imported.IsDeleted = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.PlayerProfiles.IgnoreQueryFilters().CountAsync(x =>
            (x.Id == marker.Id || x.Id == ordinary.Id || x.Id == imported.Id) && x.IsDeleted))
            .Should().Be(3, "the lookup must exercise persisted soft-deleted rows");
        var repository = provider.GetRequiredService<IPlayerProfileRepository>();

        var byUserId = await repository.ListAccountDeletedProfilesAsync(
            new[] { marker, ordinary, imported, active }.Select(x => x.PickupPalUserId ?? "missing").ToArray(), [], []);
        var byPhone = await repository.ListAccountDeletedProfilesAsync([], [marker.PhoneNumberHash ?? "missing"], []);
        var byJid = await repository.ListAccountDeletedProfilesAsync([], [], [marker.WhatsAppJidHash ?? "missing"]);
        var unrelated = await repository.ListAccountDeletedProfilesAsync([$"unknown-{Guid.NewGuid():N}"], [], []);

        byUserId.Should().ContainSingle(x => x.Id == marker.Id);
        byPhone.Should().ContainSingle(x => x.Id == marker.Id);
        byJid.Should().ContainSingle(x => x.Id == marker.Id);
        unrelated.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_WhenDeletionCommitsAfterImportRead_DoesNotRestoreProfile(bool detached)
    {
        using var provider = new ServiceCollection().AddInfrastructure(database.ConnectionString).BuildServiceProvider();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();
        var profile = Profile(null);
        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();
        var repository = provider.GetRequiredService<IPlayerProfileRepository>();
        if (detached) db.ChangeTracker.Clear();
        await using (var deletionDb = database.CreateDbContext())
        {
            var deleting = await deletionDb.PlayerProfiles.SingleAsync(x => x.Id == profile.Id);
            deleting.IsDeleted = true;
            await deletionDb.SaveChangesAsync();
        }

        profile.DisplayName = "Stale import name update";
        repository.Update(profile);
        await db.SaveChangesAsync();

        await using var verificationDb = database.CreateDbContext();
        var saved = await verificationDb.PlayerProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == profile.Id);
        saved.IsDeleted.Should().BeTrue();
        saved.DisplayName.Should().Be(profile.DisplayName);
    }

    private static PlayerProfile Profile(Guid? identityUserId) => new()
    {
        Id = Guid.NewGuid(), IdentityUserId = identityUserId,
        PickupPalUserId = $"lookup-{Guid.NewGuid():N}", PhoneNumberHash = Guid.NewGuid().ToString("N"),
        WhatsAppJidHash = Guid.NewGuid().ToString("N"), DisplayName = "Lookup player",
        NormalizedDisplayName = "LOOKUP PLAYER", PreferredPosition = string.Empty,
    };
}
