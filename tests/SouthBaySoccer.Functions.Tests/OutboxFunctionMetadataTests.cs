using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Outbox;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class OutboxFunctionMetadataTests
{
    [Fact]
    public void ProcessOutbox_WhenReflected_IsAFiveMinuteTimerTriggerAndNotAnHttpEndpoint()
    {
        var method = typeof(OutboxFunctions).GetMethod(nameof(OutboxFunctions.ProcessOutbox))
            ?? throw new InvalidOperationException("Missing ProcessOutbox.");

        method.GetCustomAttribute<FunctionAttribute>()!.Name.Should().Be(nameof(OutboxFunctions.ProcessOutbox));
        var timer = method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<TimerTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null);
        timer.Should().NotBeNull();
        timer!.Schedule.Should().Be("0 */5 * * * *");
        method.GetParameters()
            .SelectMany(parameter => parameter.GetCustomAttributes<HttpTriggerAttribute>())
            .Should().BeEmpty("the HTTP pipeline and its access markers do not apply to the timer");
        method.GetCustomAttribute<RequirePolicyAttribute>().Should().BeNull();
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public async Task ProcessOutbox_WhenDisabled_NeverClaimsRows()
    {
        var repository = new Mock<IOutboxMessageRepository>(MockBehavior.Strict);
        var function = new OutboxFunctions(
            CreateProcessor(repository.Object),
            Options.Create(new OutboxOptions { Enabled = false }),
            NullLogger<OutboxFunctions>.Instance);

        await function.ProcessOutbox(new TimerInfo(), CancellationToken.None);

        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessOutbox_WhenEnabled_RunsTheProcessor()
    {
        var repository = new Mock<IOutboxMessageRepository>();
        repository
            .Setup(x => x.ClaimDueAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var function = new OutboxFunctions(
            CreateProcessor(repository.Object),
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxFunctions>.Instance);

        await function.ProcessOutbox(new TimerInfo(), CancellationToken.None);

        repository.Verify(
            x => x.ClaimDueAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void OutboxOptions_WhenDefaulted_MatchTheStoryDesign()
    {
        var options = new OutboxOptions();

        options.Enabled.Should().BeTrue();
        options.BatchSize.Should().Be(50);
        options.LockDuration.Should().Be(TimeSpan.FromMinutes(5));
        options.MaxAttempts.Should().Be(6);
    }

    private static OutboxProcessor CreateProcessor(IOutboxMessageRepository repository)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTime(2026, 9, 16, 20, 0, 0, DateTimeKind.Utc));
        return new OutboxProcessor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            clock.Object,
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);
    }
}
