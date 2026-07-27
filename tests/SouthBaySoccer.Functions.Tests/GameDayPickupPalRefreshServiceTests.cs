using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Sessions;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class GameDayPickupPalRefreshServiceTests
{
    private static readonly DateTime BaseUtc = new(2026, 07, 26, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RefreshIfStaleAsync_WithinFreshnessWindow_ImportsOnlyOnce()
    {
        var gamesClient = CreateEmptyGamesClient();
        var service = CreateService(gamesClient.Object, () => BaseUtc);

        await service.RefreshIfStaleAsync(CancellationToken.None);
        await service.RefreshIfStaleAsync(CancellationToken.None);

        gamesClient.Verify(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_AfterFreshnessWindowElapses_ImportsAgain()
    {
        var nowUtc = BaseUtc;
        var gamesClient = CreateEmptyGamesClient();
        var service = CreateService(gamesClient.Object, () => nowUtc);

        await service.RefreshIfStaleAsync(CancellationToken.None);
        nowUtc = BaseUtc.AddSeconds(61);
        await service.RefreshIfStaleAsync(CancellationToken.None);

        gamesClient.Verify(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshIfStaleAsync_WhenImportFails_ThrottlesTheNextAttempt()
    {
        var gamesClient = new Mock<IPickupPalGamesClient>();
        gamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Pickup Pal unavailable."));
        var service = CreateService(gamesClient.Object, () => BaseUtc);

        var act = async () =>
        {
            await service.RefreshIfStaleAsync(CancellationToken.None);
            await service.RefreshIfStaleAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
        gamesClient.Verify(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_WhileAnotherRequestImports_ReturnsWithoutWaiting()
    {
        var nowUtc = BaseUtc;
        var importStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseImport = new TaskCompletionSource<IReadOnlyList<PickupPalGame>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gamesClient = new Mock<IPickupPalGamesClient>();
        gamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                importStarted.TrySetResult();
                return await releaseImport.Task;
            });
        var service = CreateService(gamesClient.Object, () => nowUtc);

        var blockedImport = service.RefreshIfStaleAsync(CancellationToken.None);
        await importStarted.Task;
        nowUtc = BaseUtc.AddSeconds(61);
        var concurrentRequest = service.RefreshIfStaleAsync(CancellationToken.None);
        var finishedFirst = await Task.WhenAny(concurrentRequest, Task.Delay(TimeSpan.FromSeconds(2)));

        finishedFirst.Should().BeSameAs(
            concurrentRequest,
            "a concurrent request must fall back to persisted sessions instead of queueing behind the in-flight import");
        blockedImport.IsCompleted.Should().BeFalse();

        releaseImport.TrySetResult([]);
        await blockedImport;
    }

    private static Mock<IPickupPalGamesClient> CreateEmptyGamesClient()
    {
        var gamesClient = new Mock<IPickupPalGamesClient>();
        gamesClient
            .Setup(x => x.GetActiveGamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return gamesClient;
    }

    private static GameDayPickupPalRefreshService CreateService(
        IPickupPalGamesClient gamesClient,
        Func<DateTime> utcNow)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(utcNow);

        var services = new ServiceCollection();
        services.AddSingleton(gamesClient);
        services.AddSingleton(Mock.Of<IPickupPalGameRepository>());
        services.AddSingleton(Mock.Of<ISessionRepository>());
        services.AddSingleton(Mock.Of<ISeasonRepository>());
        services.AddSingleton(Mock.Of<IVenueRepository>());
        services.AddSingleton(Mock.Of<IPlayerProfileRepository>());
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        services.AddSingleton(clock.Object);
        services.AddTransient<ImportPickupPalGamesCommandHandler>();
        var provider = services.BuildServiceProvider();

        return new GameDayPickupPalRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock.Object,
            NullLogger<GameDayPickupPalRefreshService>.Instance);
    }
}
