using FluentAssertions;
using SouthBaySoccer.Services;

namespace SouthBaySoccer.Client.Tests;

public sealed class AppLifecycleStateTests
{
    [Fact]
    public async Task SetActive_WhenAppStops_CancelsEpochAndWaitsUntilResume()
    {
        var lifecycle = new AppLifecycleState();
        var activeToken = await lifecycle.WaitForActiveTokenAsync(CancellationToken.None);

        lifecycle.SetActive(false);
        var resumed = lifecycle.WaitForActiveTokenAsync(CancellationToken.None);

        activeToken.IsCancellationRequested.Should().BeTrue();
        resumed.IsCompleted.Should().BeFalse();

        lifecycle.SetActive(true);
        var resumedToken = await resumed;

        resumedToken.IsCancellationRequested.Should().BeFalse();
    }
}
