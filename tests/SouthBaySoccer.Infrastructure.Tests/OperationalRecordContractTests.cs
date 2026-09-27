using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Entities.Payments;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Persistence;
using SouthBaySoccer.Infrastructure.Persistence.Interceptors;
using Xunit;
using Microsoft.Extensions.Caching.Memory;
using SouthBaySoccer.Infrastructure.Caching;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class OperationalRecordContractTests
{
    private readonly InfrastructureDatabaseFixture database;

    public OperationalRecordContractTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task RefreshTokens_WhenTokenHashIsDuplicated_DatabaseRejectsDuplicate()
    {
        using var db = database.CreateDbContext();
        var tokenHash = $"rt_{Guid.NewGuid():N}";

        db.RefreshTokens.Add(CreateRefreshToken(tokenHash));
        db.RefreshTokens.Add(CreateRefreshToken(tokenHash));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RefreshTokens_WhenReuseIsMarkedBeforeConsumption_DatabaseRejectsInvalidState()
    {
        using var db = database.CreateDbContext();
        var token = CreateRefreshToken($"rt_{Guid.NewGuid():N}");
        token.ReuseDetectedAtUtc = new DateTime(2026, 6, 26, 20, 0, 0, DateTimeKind.Utc);
        db.RefreshTokens.Add(token);
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProcessedWebhookEvents_WhenProviderEventIsDuplicated_DatabaseRejectsDuplicate()
    {
        using var db = database.CreateDbContext();
        var providerEventId = $"evt_{Guid.NewGuid():N}";

        db.ProcessedWebhookEvents.Add(CreateProcessedWebhookEvent(providerEventId));
        db.ProcessedWebhookEvents.Add(CreateProcessedWebhookEvent(providerEventId));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task OutboxMessages_WhenIdempotencyKeyIsDuplicated_DatabaseRejectsDuplicate()
    {
        using var db = database.CreateDbContext();
        var idempotencyKey = $"outbox_{Guid.NewGuid():N}";

        db.OutboxMessages.Add(CreateOutboxMessage(idempotencyKey));
        db.OutboxMessages.Add(CreateOutboxMessage(idempotencyKey));
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData(typeof(RefreshToken))]
    [InlineData(typeof(OutboxMessage))]
    public async Task SaveChangesAsync_WhenImmutableOperationalRecordRemoved_BlocksHardDelete(Type entityType)
    {
        using var db = CreateInterceptedDbContext();
        object entity = entityType == typeof(RefreshToken)
            ? CreateRefreshToken($"rt_{Guid.NewGuid():N}")
            : CreateOutboxMessage($"outbox_{Guid.NewGuid():N}");
        db.Add(entity);
        await db.SaveChangesAsync();

        db.Remove(entity);
        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Hard deletes are not allowed for {entityType.Name}*");
    }

    private static RefreshToken CreateRefreshToken(string tokenHash)
    {
        return new RefreshToken
        {
            IdentityUserId = Guid.NewGuid(),
            PlayerProfileId = Guid.NewGuid(),
            TokenHash = tokenHash,
            FamilyId = Guid.NewGuid(),
            DeviceId = $"device-{Guid.NewGuid():N}",
            UserAgentHash = $"ua-{Guid.NewGuid():N}",
            IpAddressHash = $"ip-{Guid.NewGuid():N}",
            ExpiresAtUtc = new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc),
        };
    }

    private static ProcessedWebhookEvent CreateProcessedWebhookEvent(string providerEventId)
    {
        return new ProcessedWebhookEvent
        {
            Provider = "Stripe",
            ProviderEventId = providerEventId,
            EventType = "invoice.paid",
            ProviderCreatedAtUtc = new DateTime(2026, 6, 26, 20, 0, 0, DateTimeKind.Utc),
            ProcessedAtUtc = new DateTime(2026, 6, 26, 20, 1, 0, DateTimeKind.Utc),
            Status = WebhookProcessingStatus.Processed,
        };
    }

    private static OutboxMessage CreateOutboxMessage(string idempotencyKey)
    {
        return new OutboxMessage
        {
            MessageType = "PlayerWaitlistPromoted",
            PayloadJson = "{}",
            Status = OutboxMessageStatus.Pending,
            AvailableAtUtc = new DateTime(2026, 6, 26, 20, 0, 0, DateTimeKind.Utc),
            IdempotencyKey = idempotencyKey,
        };
    }

    private SouthBaySoccerDbContext CreateInterceptedDbContext()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 6, 26, 20, 0, 0, DateTimeKind.Utc));

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);

        return database.CreateDbContext(new AuditSoftDeleteSaveChangesInterceptor(clock.Object, currentUser.Object, new CacheEvictionQueue(new MemoryCache(new MemoryCacheOptions()))));
    }
}
