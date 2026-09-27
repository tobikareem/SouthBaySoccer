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

        var deletion = await service.DeleteAsync(identityUser.Id, (_, _) => Task.CompletedTask);

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

        var deletion = await service.DeleteAsync(Guid.NewGuid(), (_, _) => Task.CompletedTask);

        deletion.PlayerProfileId.Should().BeNull();
        deletion.PickupPalUserId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenOutboxWriteFails_RollsBackProfileIdentityAndTokenChanges()
    {
        using var provider = CreateServiceProvider();
        var db = provider.GetRequiredService<SouthBaySoccerDbContext>();
        var manager = provider.GetRequiredService<UserManager<ApplicationIdentityUser>>();
        var user = new ApplicationIdentityUser
        {
            Id = Guid.NewGuid(),
            UserName = $"atomic-delete-{Guid.NewGuid():N}",
            Email = $"atomic-delete-{Guid.NewGuid():N}@example.test",
        };
        (await manager.CreateAsync(user)).Succeeded.Should().BeTrue();
        var originalEmail = user.Email;
        var profile = new PlayerProfile
        {
            Id = Guid.NewGuid(), IdentityUserId = user.Id,
            PickupPalUserId = $"atomic-{Guid.NewGuid():N}",
            DisplayName = "Atomic Delete", NormalizedDisplayName = "ATOMIC DELETE",
            PreferredPosition = string.Empty,
        };
        db.PlayerProfiles.Add(profile);
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(), IdentityUserId = user.Id, PlayerProfileId = profile.Id,
            TokenHash = $"atomic-{Guid.NewGuid():N}", FamilyId = Guid.NewGuid(),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        };
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();
        var key = $"atomic-delete:{user.Id:D}";
        var service = provider.GetRequiredService<ILocalAccountDeletionService>();
        async Task RecordIntent(LocalAccountDeletion deletion, CancellationToken token)
        {
            if (await db.OutboxMessages.AnyAsync(x => x.IdempotencyKey == key, token))
            {
                return;
            }

            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(), MessageType = OnboardingOutboxMessages.PickupPalUserDeletionRequested,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(deletion),
                IdempotencyKey = key, AvailableAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(token);
        }

        var act = () => service.DeleteAsync(user.Id, async (deletion, token) =>
        {
            await RecordIntent(deletion, token);
            throw new InvalidOperationException("Injected failure after outbox save and before commit.");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using (var assertionDb = database.CreateDbContext())
        {
            (await assertionDb.PlayerProfiles.SingleAsync(x => x.Id == profile.Id)).IsDeleted.Should().BeFalse();
            (await assertionDb.Users.SingleAsync(x => x.Id == user.Id)).Email.Should().Be(originalEmail);
            (await assertionDb.RefreshTokens.SingleAsync(x => x.Id == refreshToken.Id)).RevokedAtUtc.Should().BeNull();
            (await assertionDb.OutboxMessages.AnyAsync(x => x.IdempotencyKey == key)).Should().BeFalse();
        }

        // A fresh execution clears rolled-back tracked state; a committed replay retains the
        // provider id and does not duplicate the durable intent even though the profile is deleted.
        var first = await service.DeleteAsync(user.Id, RecordIntent);
        var repeated = await service.DeleteAsync(user.Id, RecordIntent);
        repeated.Should().Be(first);
        repeated.PickupPalUserId.Should().Be(profile.PickupPalUserId);
        await using var committedDb = database.CreateDbContext();
        (await committedDb.PlayerProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == profile.Id)).IsDeleted.Should().BeTrue();
        (await committedDb.RefreshTokens.SingleAsync(x => x.Id == refreshToken.Id)).RevokedAtUtc.Should().NotBeNull();
        (await committedDb.OutboxMessages.CountAsync(x => x.IdempotencyKey == key)).Should().Be(1);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }
}
