using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Infrastructure;
using Xunit;

namespace SouthBaySoccer.Infrastructure.Tests;

[Collection(InfrastructureDatabaseCollection.Name)]
public sealed class IdempotencyStoreTests
{
    private static readonly DateTime Now = new(2026, 6, 27, 4, 0, 0, DateTimeKind.Utc);
    private readonly InfrastructureDatabaseFixture database;

    public IdempotencyStoreTests(InfrastructureDatabaseFixture database)
    {
        this.database = database;
    }

    [Fact]
    public async Task CompleteAsync_WhenRecordExists_PersistsReplayableResponseMetadata()
    {
        using var provider = CreateServiceProvider(Now);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var identityUserId = Guid.NewGuid();

        var key = $"key-{Guid.NewGuid():N}";
        var record = await store.CreateAsync(
            identityUserId,
            playerProfileId: null,
            "SubmitRsvp",
            key,
            "request-hash",
            Now.AddHours(24));
        await store.CompleteAsync(record.Id, 200, "{\"state\":\"Going\"}", "response-hash", Now);

        var stored = await store.FindAsync(identityUserId, "SubmitRsvp", key);

        stored.Should().NotBeNull();
        stored!.RequestHash.Should().Be("request-hash");
        stored.ResponseStatusCode.Should().Be(200);
        stored.ResponseBodyJson.Should().Be("{\"state\":\"Going\"}");
        stored.CompletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task CreateAsync_WhenExpiredRecordExists_ReusesKeyForRetry()
    {
        using var provider = CreateServiceProvider(Now);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var identityUserId = Guid.NewGuid();
        var key = $"key-{Guid.NewGuid():N}";
        var first = await store.CreateAsync(identityUserId, null, "CancelRsvp", key, "old-hash", Now.AddHours(1));
        await store.AbandonAsync(first.Id, Now.AddSeconds(-1));

        var retry = await store.CreateAsync(identityUserId, null, "CancelRsvp", key, "new-hash", Now.AddHours(24));
        var stored = await store.FindAsync(identityUserId, "CancelRsvp", key);

        retry.Id.Should().Be(first.Id);
        stored!.RequestHash.Should().Be("new-hash");
        stored.CompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenActiveRecordExists_ThrowsConflict()
    {
        using var provider = CreateServiceProvider(Now);
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var identityUserId = Guid.NewGuid();
        var key = $"key-{Guid.NewGuid():N}";
        await store.CreateAsync(identityUserId, null, "SubmitRsvp", key, "request-hash", Now.AddHours(24));

        var act = () => store.CreateAsync(identityUserId, null, "SubmitRsvp", key, "request-hash", Now.AddHours(24));

        await act.Should().ThrowAsync<ApplicationConflictException>();
    }

    private ServiceProvider CreateServiceProvider(DateTime now)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);

        var services = new ServiceCollection();
        services.AddSingleton(clock.Object);
        services.AddScoped(_ => currentUser.Object);
        services.AddInfrastructure(database.ConnectionString);
        return services.BuildServiceProvider();
    }
}
