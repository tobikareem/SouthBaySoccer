using System.Collections.Concurrent;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests.TestSupport;

internal sealed class ControlledPollingDelay : IPollingDelay
{
    private readonly ConcurrentQueue<TaskCompletionSource> gates = new();
    private readonly ConcurrentQueue<TimeSpan> delays = new();

    public IReadOnlyList<TimeSpan> Delays => delays.ToArray();

    public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        delays.Enqueue(delay);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gates.Enqueue(gate);
        await gate.Task.WaitAsync(cancellationToken);
    }

    public void ReleaseNext()
    {
        if (!gates.TryDequeue(out var gate))
        {
            throw new InvalidOperationException("No polling delay is waiting.");
        }

        gate.SetResult();
    }

    public async Task WaitForDelayCountAsync(int count)
    {
        for (var attempt = 0; attempt < 100 && Delays.Count < count; attempt++)
        {
            await Task.Delay(10);
        }

        if (Delays.Count < count)
        {
            throw new TimeoutException($"Expected {count} polling delays, observed {Delays.Count}.");
        }
    }
}
