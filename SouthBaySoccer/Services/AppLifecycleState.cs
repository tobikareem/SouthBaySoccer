namespace SouthBaySoccer.Services;

/// <summary>Coordinates foreground-only work with the current application-active epoch.</summary>
public interface IAppLifecycleState
{
    Task<CancellationToken> WaitForActiveTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Exposes a token cancelled when the app leaves the foreground and a wait that completes on resume.
/// </summary>
public sealed class AppLifecycleState : IAppLifecycleState
{
    private readonly object syncRoot = new();
    private CancellationTokenSource activeCancellation = new();
    private TaskCompletionSource<CancellationToken>? resumed;
    private bool isActive = true;

    public Task<CancellationToken> WaitForActiveTokenAsync(CancellationToken cancellationToken)
    {
        lock (syncRoot)
        {
            if (isActive)
            {
                return Task.FromResult(activeCancellation.Token);
            }

            resumed ??= new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            return resumed.Task.WaitAsync(cancellationToken);
        }
    }

    public void SetActive(bool active)
    {
        TaskCompletionSource<CancellationToken>? waiter = null;
        CancellationToken token = default;
        CancellationTokenSource? retired = null;
        lock (syncRoot)
        {
            if (isActive == active)
            {
                return;
            }

            isActive = active;
            if (!active)
            {
                retired = activeCancellation;
                resumed = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                activeCancellation = new CancellationTokenSource();
                token = activeCancellation.Token;
                waiter = resumed;
                resumed = null;
            }
        }

        retired?.Cancel();
        retired?.Dispose();
        waiter?.TrySetResult(token);
    }
}

internal sealed class AlwaysActiveAppLifecycleState : IAppLifecycleState
{
    public static AlwaysActiveAppLifecycleState Instance { get; } = new();

    public Task<CancellationToken> WaitForActiveTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(cancellationToken);
}
