using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;

namespace SouthBaySoccer.Functions.Sessions;

/// <summary>
/// Refreshes Pickup Pal game data in an isolated dependency-injection scope. The short freshness
/// window prevents repeated provider calls when members open Sessions or Game Day together, and
/// only the single request that wins the gate pays for the import — every concurrent request
/// returns immediately and serves persisted sessions.
/// </summary>
public sealed class GameDayPickupPalRefreshService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<GameDayPickupPalRefreshService> logger)
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private long lastAttemptTicksUtc;

    public async Task RefreshIfStaleAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(clock.UtcNow))
        {
            return;
        }

        // Zero-wait acquire: when another request is already importing, fall back to persisted
        // sessions instead of queueing behind its provider call.
        if (!await refreshGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return;
        }

        try
        {
            var nowUtc = clock.UtcNow;
            if (IsFresh(nowUtc))
            {
                return;
            }

            // Throttle failures too; otherwise an unavailable provider would be hammered by every
            // member opening the tab. The next request after the freshness window retries.
            Volatile.Write(ref lastAttemptTicksUtc, nowUtc.Ticks);
            using var importCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            importCts.CancelAfter(ImportTimeout);
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ImportPickupPalGamesCommandHandler>();
            var result = await handler.HandleAsync(importCts.Token);
            if (result.Warnings.Count > 0)
            {
                logger.LogWarning(
                    "Pickup Pal session refresh completed with {WarningCount} warning(s).",
                    result.Warnings.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Pickup Pal session refresh exceeded {TimeoutSeconds}s.", ImportTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pickup Pal session refresh failed; using persisted sessions.");
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private bool IsFresh(DateTime nowUtc)
    {
        var lastAttemptTicks = Volatile.Read(ref lastAttemptTicksUtc);
        return lastAttemptTicks != 0 && nowUtc.Ticks - lastAttemptTicks < FreshnessWindow.Ticks;
    }
}
