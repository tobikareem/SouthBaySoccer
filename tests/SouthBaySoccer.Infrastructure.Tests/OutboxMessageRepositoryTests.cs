using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure;
using SouthBaySoccer.Infrastructure.Persistence;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

/// <summary>LocalDB-bound (Windows CI), like the other repository suites.</summary>
[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class OutboxMessageRepositoryTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;

    public OutboxMessageRepositoryTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task ClaimDueAsync_WhenRowsVary_ClaimsOnlyDueRowsOfTheRequestedTypes()
    {
        var type = $"Type_{Guid.NewGuid():N}";
        var otherType = $"Other_{Guid.NewGuid():N}";
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var duePending = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-1));
        var dueRetry = Message(type, OutboxMessageStatus.RetryScheduled, NowUtc.AddMinutes(-1));
        var notYetDue = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(1));
        var processed = Message(type, OutboxMessageStatus.Processed, NowUtc.AddMinutes(-1));
        var deadLettered = Message(type, OutboxMessageStatus.DeadLettered, NowUtc.AddMinutes(-1));
        var expiredLock = Message(type, OutboxMessageStatus.Processing, NowUtc.AddMinutes(-5), lockToken: "old", lockedUntilUtc: NowUtc.AddMinutes(-1));
        var liveLock = Message(type, OutboxMessageStatus.Processing, NowUtc.AddMinutes(-5), lockToken: "other", lockedUntilUtc: NowUtc.AddMinutes(4));
        var wrongType = Message(otherType, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-1));
        db.OutboxMessages.AddRange(duePending, dueRetry, notYetDue, processed, deadLettered, expiredLock, liveLock, wrongType);
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();

        var claimed = await repository.ClaimDueAsync([type], NowUtc, "run-1", NowUtc.AddMinutes(5), batchSize: 50);

        claimed.Select(x => x.Id).Should().BeEquivalentTo([duePending.Id, dueRetry.Id, expiredLock.Id]);
        claimed.Should().OnlyContain(x =>
            x.Status == OutboxMessageStatus.Processing
            && x.LockToken == "run-1"
            && x.LockedUntilUtc == NowUtc.AddMinutes(5));
        var untouched = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.Id == liveLock.Id || x.Id == wrongType.Id || x.Id == notYetDue.Id)
            .ToListAsync();
        untouched.Should().OnlyContain(x => x.LockToken != "run-1");
    }

    [Fact]
    public async Task ClaimDueAsync_WhenTwoRunsRaceForOneRow_ExactlyOneWins()
    {
        var type = $"Type_{Guid.NewGuid():N}";
        using var provider = CreateServiceProvider();
        using var seedScope = provider.CreateScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var row = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-1));
        seed.OutboxMessages.Add(row);
        await seed.SaveChangesAsync();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
        var repositoryB = scopeB.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();

        var claimedA = await repositoryA.ClaimDueAsync([type], NowUtc, "run-a", NowUtc.AddMinutes(5), 50);
        var claimedB = await repositoryB.ClaimDueAsync([type], NowUtc, "run-b", NowUtc.AddMinutes(5), 50);

        claimedA.Select(x => x.Id).Should().BeEquivalentTo([row.Id]);
        claimedB.Should().BeEmpty();
        var persisted = await seed.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        persisted.LockToken.Should().Be("run-a");
    }

    [Fact]
    public async Task ClaimDueAsync_WhenBatchSizeIsSmaller_ClaimsTheOldestDueRowsFirst()
    {
        var type = $"Type_{Guid.NewGuid():N}";
        using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var older = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-10));
        var newer = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-1));
        db.OutboxMessages.AddRange(newer, older);
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();

        var claimed = await repository.ClaimDueAsync([type], NowUtc, "run-1", NowUtc.AddMinutes(5), batchSize: 1);

        claimed.Should().ContainSingle().Which.Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTwoWritersUpdateOneRow_SecondWriterGetsConcurrencyConflict()
    {
        var type = $"Type_{Guid.NewGuid():N}";
        using var provider = CreateServiceProvider();
        using var seedScope = provider.CreateScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var row = Message(type, OutboxMessageStatus.Pending, NowUtc.AddMinutes(-1));
        seed.OutboxMessages.Add(row);
        await seed.SaveChangesAsync();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<SouthBaySoccerDbContext>();
        var rowA = await dbA.OutboxMessages.SingleAsync(x => x.Id == row.Id);
        var rowB = await dbB.OutboxMessages.SingleAsync(x => x.Id == row.Id);

        rowA.Status = OutboxMessageStatus.Processing;
        await dbA.SaveChangesAsync();
        rowB.Status = OutboxMessageStatus.Processed;
        var act = () => dbB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private ServiceProvider CreateServiceProvider()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }

    private static OutboxMessage Message(
        string type,
        OutboxMessageStatus status,
        DateTime availableAtUtc,
        string? lockToken = null,
        DateTime? lockedUntilUtc = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            MessageType = type,
            PayloadJson = "{}",
            Status = status,
            AvailableAtUtc = availableAtUtc,
            LockToken = lockToken,
            LockedUntilUtc = lockedUntilUtc,
        };
}
