using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Persistence;
using SouthBaySoccer.Infrastructure.Persistence.Interceptors;
using Xunit;
using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Infrastructure.Caching;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class AuditSoftDeleteInterceptorTests
{
    private readonly InfrastructureDatabaseFixture database;

    public AuditSoftDeleteInterceptorTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityAdded_StampsCreatedAuditFields()
    {
        var actorId = Guid.NewGuid();
        var now = new DateTime(2026, 6, 26, 1, 2, 3, DateTimeKind.Utc);
        using var db = CreateDbContext(() => now, actorId);

        var profile = CreatePlayerProfile();

        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();

        profile.CreatedAt.Should().Be(now);
        profile.CreatedBy.Should().Be(actorId.ToString("D"));
        profile.UpdatedAt.Should().BeNull();
        profile.UpdatedBy.Should().BeNull();
        profile.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityModified_StampsUpdatedAuditFieldsWithoutChangingCreateAudit()
    {
        var actorId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 26, 1, 2, 3, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 6, 26, 2, 3, 4, DateTimeKind.Utc);
        var currentTime = createdAt;
        using var db = CreateDbContext(() => currentTime, actorId);
        var profile = CreatePlayerProfile();
        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();

        currentTime = updatedAt;
        profile.PreferredPosition = "Midfielder";
        await db.SaveChangesAsync();

        profile.CreatedAt.Should().Be(createdAt);
        profile.CreatedBy.Should().Be(actorId.ToString("D"));
        profile.UpdatedAt.Should().Be(updatedAt);
        profile.UpdatedBy.Should().Be(actorId.ToString("D"));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenMutableEntityRemoved_SoftDeletesAndQueryFilterHidesRecord()
    {
        var actorId = Guid.NewGuid();
        var deletedAt = new DateTime(2026, 6, 26, 3, 4, 5, DateTimeKind.Utc);
        using var db = CreateDbContext(() => deletedAt, actorId);
        var profile = CreatePlayerProfile();
        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();

        db.PlayerProfiles.Remove(profile);
        await db.SaveChangesAsync();

        (await db.PlayerProfiles.AnyAsync(x => x.Id == profile.Id)).Should().BeFalse();
        var persisted = await db.PlayerProfiles.IgnoreQueryFilters().SingleAsync(x => x.Id == profile.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.UpdatedAt.Should().Be(deletedAt);
        persisted.UpdatedBy.Should().Be(actorId.ToString("D"));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenImmutableEntityRemoved_BlocksAccidentalHardDelete()
    {
        var webhook = new ProcessedWebhookEvent
        {
            Provider = "Stripe",
            ProviderEventId = $"evt_{Guid.NewGuid():N}",
            EventType = "checkout.session.completed",
            ProviderCreatedAtUtc = new DateTime(2026, 6, 26, 4, 5, 6, DateTimeKind.Utc),
            ProcessedAtUtc = new DateTime(2026, 6, 26, 4, 6, 7, DateTimeKind.Utc),
            Status = WebhookProcessingStatus.Processed,
        };
        using var db = CreateDbContext(() => webhook.ProcessedAtUtc, Guid.NewGuid());
        db.ProcessedWebhookEvents.Add(webhook);
        await db.SaveChangesAsync();

        db.ProcessedWebhookEvents.Remove(webhook);
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Hard deletes are not allowed for ProcessedWebhookEvent*");
        db.Entry(webhook).State = EntityState.Unchanged;
        (await db.ProcessedWebhookEvents.AnyAsync(x => x.Id == webhook.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenActorIsAnonymous_StampsSystemActor()
    {
        using var db = CreateDbContext(() => new DateTime(2026, 6, 26, 5, 6, 7, DateTimeKind.Utc), null);
        var profile = CreatePlayerProfile();

        db.PlayerProfiles.Add(profile);
        await db.SaveChangesAsync();

        profile.CreatedBy.Should().Be("System");
    }

    private static PlayerProfile CreatePlayerProfile()
    {
        var name = $"Player {Guid.NewGuid():N}";
        return new PlayerProfile
        {
            DisplayName = name,
            NormalizedDisplayName = name.ToUpperInvariant(),
            PreferredPosition = "Forward",
            Role = PlayerRole.Player,
        };
    }

    private SouthBaySoccerDbContext CreateDbContext(Func<DateTime> utcNow, Guid? userId)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(utcNow);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(userId.HasValue);

        return database.CreateDbContext(new AuditSoftDeleteSaveChangesInterceptor(clock.Object, currentUser.Object, new CacheEvictionQueue(new MemoryCache(new MemoryCacheOptions()))));
    }
}