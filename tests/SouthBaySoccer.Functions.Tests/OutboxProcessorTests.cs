using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Outbox;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Outbox;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class OutboxProcessorTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunAsync_WhenRowsAreDue_ClaimsTheHandledTypesWithLockTokenAndExpiry()
    {
        var context = new TestContext();

        await context.Processor.RunAsync();

        context.Repository.Verify(
            x => x.ClaimDueAsync(
                It.Is<IReadOnlyCollection<string>>(types => types.SequenceEqual(OutboxMessageTypes.Handled)),
                NowUtc,
                It.Is<string>(token => !string.IsNullOrWhiteSpace(token)),
                NowUtc.AddMinutes(5),
                50,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void HandledTypes_WhenRegistered_MatchTheHandlersTheCompositionRootRegisters()
    {
        // The static claim list and the DI registrations must stay in sync; a handler that is
        // registered but not listed would never have its rows claimed.
        OutboxMessageTypes.Handled.Should().BeEquivalentTo(
        [
            new RsvpPickupPalSyncOutboxHandler(Mock.Of<SouthBaySoccer.Application.Features.Rsvps.IRsvpPickupPalSyncService>()).MessageType,
            new SessionPickupPalSyncOutboxHandler(Mock.Of<SouthBaySoccer.Application.Features.Scheduling.ISessionPickupPalSyncService>()).MessageType,
            new PickupPalUserDeletionOutboxHandler(Mock.Of<SouthBaySoccer.Application.Features.Onboarding.IPickupPalOnboardingClient>()).MessageType,
        ]);
    }

    [Fact]
    public async Task RunAsync_WhenSettleLosesTheRowVersionRace_SkipsTheRowWithoutThrowing()
    {
        var context = new TestContext();
        var message = context.Due("TypeA");
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Completed()));
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("row version"));

        var summary = await context.Processor.RunAsync();

        summary.Should().Be(new OutboxRunSummary(1, 0, 0, 0));
        context.Repository.Verify(x => x.Update(message), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenHandlerThrows_SettlesTheRowInAFreshScope()
    {
        var handlerScopes = new List<ScopeProbe>();
        var settleScopes = new List<ScopeProbe>();
        var repository = new Mock<IOutboxMessageRepository>();
        var message = new OutboxMessage { Id = Guid.NewGuid(), MessageType = "TypeA", PayloadJson = "{}", Status = OutboxMessageStatus.Processing };
        repository
            .Setup(x => x.ClaimDueAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IOutboxMessageHandler>(provider =>
        {
            handlerScopes.Add(provider.GetRequiredService<ScopeProbe>());
            return HandlerFor("TypeA", _ => throw new InvalidOperationException("boom"));
        });
        services.AddScoped(provider =>
        {
            settleScopes.Add(provider.GetRequiredService<ScopeProbe>());
            return repository.Object;
        });
        services.AddScoped(_ => Mock.Of<IUnitOfWork>());
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
        var processor = new OutboxProcessor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock.Object,
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

        var summary = await processor.RunAsync();

        summary.Retried.Should().Be(1);
        handlerScopes.Should().ContainSingle();
        // Claim scope, then the settle scope: neither is the scope the throwing handler ran in.
        settleScopes.Should().HaveCount(2);
        settleScopes.Should().NotContain(handlerScopes[0]);
    }

    private sealed class ScopeProbe;

    [Fact]
    public async Task RunAsync_WhenHandlerCompletes_MarksProcessedClearsLockAndSaves()
    {
        var context = new TestContext();
        var message = context.Due("TypeA");
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Completed()));

        var summary = await context.Processor.RunAsync();

        summary.Should().Be(new OutboxRunSummary(1, 1, 0, 0));
        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(NowUtc);
        message.LockToken.Should().BeNull();
        message.LockedUntilUtc.Should().BeNull();
        context.Repository.Verify(x => x.Update(message), Times.Once);
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 5)]
    [InlineData(2, 3, 15)]
    [InlineData(3, 4, 60)]
    [InlineData(4, 5, 60)]
    public async Task RunAsync_WhenHandlerAsksForRetry_ReschedulesWithBackoff(
        int attemptsSoFar,
        int expectedAttemptCount,
        int expectedDelayMinutes)
    {
        var context = new TestContext();
        var message = context.Due("TypeA", attemptCount: attemptsSoFar);
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Retry("Unavailable")));

        var summary = await context.Processor.RunAsync();

        summary.Retried.Should().Be(1);
        message.Status.Should().Be(OutboxMessageStatus.RetryScheduled);
        message.AttemptCount.Should().Be(expectedAttemptCount);
        message.AvailableAtUtc.Should().Be(NowUtc.AddMinutes(expectedDelayMinutes));
        message.LockToken.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenSixthAttemptFails_DeadLettersWithSafeReason()
    {
        var context = new TestContext();
        var message = context.Due("TypeA", attemptCount: 5);
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Retry("Unavailable")));

        var summary = await context.Processor.RunAsync();

        summary.DeadLettered.Should().Be(1);
        message.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        message.AttemptCount.Should().Be(6);
        message.DeadLetterReason.Should().Be("MaxAttemptsExceeded:Unavailable");
    }

    [Fact]
    public async Task RunAsync_WhenHandlerFailsPermanently_DeadLettersImmediately()
    {
        var context = new TestContext();
        var message = context.Due("TypeA");
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Fail("InvalidPayload")));

        await context.Processor.RunAsync();

        message.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        message.DeadLetterReason.Should().Be("InvalidPayload");
        message.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenHandlerThrows_TreatsItAsRetryRecordingOnlyTheExceptionType()
    {
        var context = new TestContext();
        var message = context.Due("TypeA");
        context.Handlers.Add(HandlerFor("TypeA", _ => throw new InvalidOperationException("secret detail")));

        await context.Processor.RunAsync();

        message.Status.Should().Be(OutboxMessageStatus.RetryScheduled);
        message.AttemptCount.Should().Be(1);
        message.DeadLetterReason.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenNoHandlerOwnsTheType_DeadLettersAsUnknown()
    {
        var context = new TestContext();
        var message = context.Due("Orphan");
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Completed()));

        await context.Processor.RunAsync();

        message.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        message.DeadLetterReason.Should().Be(OutboxProcessor.UnknownMessageTypeCode);
    }

    [Fact]
    public async Task RunAsync_WhenOneSaveFails_StillSettlesTheRemainingRows()
    {
        var context = new TestContext();
        var first = context.Due("TypeA");
        var second = context.Due("TypeA");
        context.Handlers.Add(HandlerFor("TypeA", _ => OutboxHandlingResult.Completed()));
        var saves = 0;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++saves == 1
                ? Task.FromException<int>(new InvalidOperationException("deadlock"))
                : Task.FromResult(1));

        var summary = await context.Processor.RunAsync();

        summary.Claimed.Should().Be(2);
        summary.Processed.Should().Be(1, "the row whose save failed is reclaimed after its lock expires");
        first.Status.Should().Be(OutboxMessageStatus.Processed);
        second.Status.Should().Be(OutboxMessageStatus.Processed);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_Propagates()
    {
        var context = new TestContext();
        context.Due("TypeA");
        using var cts = new CancellationTokenSource();
        context.Handlers.Add(HandlerFor("TypeA", _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        var act = () => context.Processor.RunAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void BackoffFor_WhenAttemptsExceedTheTable_ReusesTheLastDelay()
    {
        OutboxProcessor.BackoffFor(0).Should().Be(TimeSpan.FromMinutes(1));
        OutboxProcessor.BackoffFor(1).Should().Be(TimeSpan.FromMinutes(1));
        OutboxProcessor.BackoffFor(4).Should().Be(TimeSpan.FromMinutes(60));
        OutboxProcessor.BackoffFor(99).Should().Be(TimeSpan.FromMinutes(60));
    }

    private static IOutboxMessageHandler HandlerFor(string type, Func<OutboxMessage, OutboxHandlingResult> handle)
    {
        var handler = new Mock<IOutboxMessageHandler>();
        handler.SetupGet(x => x.MessageType).Returns(type);
        handler
            .Setup(x => x.HandleAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Returns<OutboxMessage, CancellationToken>((message, _) => Task.FromResult(handle(message)));
        return handler.Object;
    }

    private sealed class TestContext
    {
        public Mock<IOutboxMessageRepository> Repository { get; } = new();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public List<IOutboxMessageHandler> Handlers { get; } = [];

        private readonly List<OutboxMessage> due = [];

        public TestContext()
        {
            Repository
                .Setup(x => x.ClaimDueAsync(
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> _, DateTime _, string token, DateTime until, int _, CancellationToken _) =>
                {
                    // Mirror the repository contract: claimed rows come back Processing with the lock.
                    // Type filtering is the repository's job (covered by its own tests), so every
                    // due row is handed out here regardless of the requested types.
                    var claimed = due.ToList();
                    foreach (var message in claimed)
                    {
                        message.Status = OutboxMessageStatus.Processing;
                        message.LockToken = token;
                        message.LockedUntilUtc = until;
                    }

                    return claimed;
                });
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public OutboxProcessor Processor
        {
            get
            {
                var clock = new Mock<IClock>();
                clock.SetupGet(x => x.UtcNow).Returns(NowUtc);
                var services = new ServiceCollection();
                services.AddSingleton(Repository.Object);
                services.AddSingleton(UnitOfWork.Object);
                foreach (var handler in Handlers)
                {
                    services.AddSingleton(handler);
                }

                var provider = services.BuildServiceProvider();
                return new OutboxProcessor(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    clock.Object,
                    Options.Create(new OutboxOptions()),
                    NullLogger<OutboxProcessor>.Instance);
            }
        }

        public OutboxMessage Due(string type, int attemptCount = 0)
        {
            var message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = type,
                PayloadJson = "{}",
                Status = attemptCount == 0 ? OutboxMessageStatus.Pending : OutboxMessageStatus.RetryScheduled,
                AvailableAtUtc = NowUtc.AddMinutes(-1),
                AttemptCount = attemptCount,
            };
            due.Add(message);
            return message;
        }
    }
}
