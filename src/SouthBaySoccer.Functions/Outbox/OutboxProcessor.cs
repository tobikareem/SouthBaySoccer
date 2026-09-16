using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Outbox;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Functions.Outbox;

/// <summary>Counts from one processor run, for the timer's log line.</summary>
public sealed record OutboxRunSummary(int Claimed, int Processed, int Retried, int DeadLettered);

/// <summary>
/// Drains due outbox rows of the types in <see cref="OutboxMessageTypes.Handled"/>. Rows are
/// claimed atomically (lock token + expiry) in one scope, then each row runs in its own
/// dependency-injection scope so a failed save cannot poison the next row's context; a handler
/// that throws is settled in a fresh scope so none of its half-tracked state is committed with the
/// row. Retryable failures back off 1, 5, 15, then 60 minutes and dead-letter once
/// <see cref="OutboxOptions.MaxAttempts"/> attempts are spent; permanent failures dead-letter
/// immediately. A settle that loses a row-version race (the immediate RSVP path reopened the row)
/// is skipped: the other writer owns the row now. Every handler must be idempotent because a crash
/// between execution and settlement re-runs the row after its lock expires.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    /// <summary>Retry delays indexed by attempt number (1-based); later attempts reuse the last entry.</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    /// <summary>Dead-letter reason for a claimed row whose type has no handler registered.</summary>
    public const string UnknownMessageTypeCode = "UnknownMessageType";

    /// <summary>Dead-letter reason prefix when a retryable failure exhausts the attempt budget.</summary>
    public const string MaxAttemptsExceededPrefix = "MaxAttemptsExceeded:";

    public async Task<OutboxRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var lockToken = Guid.NewGuid().ToString("N");

        IReadOnlyList<OutboxMessage> claimed;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var nowUtc = clock.UtcNow;
            claimed = await claimScope.ServiceProvider
                .GetRequiredService<IOutboxMessageRepository>()
                .ClaimDueAsync(
                    OutboxMessageTypes.Handled,
                    nowUtc,
                    lockToken,
                    nowUtc.Add(settings.LockDuration),
                    settings.BatchSize,
                    cancellationToken);
        }

        var processed = 0;
        var retried = 0;
        var deadLettered = 0;
        foreach (var message in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await ExecuteAsync(message, settings, cancellationToken);
            switch (status)
            {
                case OutboxMessageStatus.Processed:
                    processed++;
                    break;
                case OutboxMessageStatus.RetryScheduled:
                    retried++;
                    break;
                case OutboxMessageStatus.DeadLettered:
                    deadLettered++;
                    break;
            }
        }

        return new OutboxRunSummary(claimed.Count, processed, retried, deadLettered);
    }

    private async Task<OutboxMessageStatus?> ExecuteAsync(
        OutboxMessage message,
        OutboxOptions settings,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var handler = scope.ServiceProvider
                .GetServices<IOutboxMessageHandler>()
                .FirstOrDefault(candidate => string.Equals(candidate.MessageType, message.MessageType, StringComparison.Ordinal));

            OutboxHandlingResult result;
            if (handler is null)
            {
                result = OutboxHandlingResult.Fail(UnknownMessageTypeCode);
            }
            else
            {
                try
                {
                    result = await handler.HandleAsync(message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Unexpected failures are retryable by default; only the exception type is
                    // recorded. The scope's context may hold whatever the handler half-applied, so
                    // the row is settled in a fresh one.
                    result = OutboxHandlingResult.Retry(exception.GetType().Name);
                    await scope.DisposeAsync();
                    scope = scopeFactory.CreateAsyncScope();
                }
            }

            Settle(message, result, settings, clock.UtcNow);
            return await SaveAsync(scope.ServiceProvider, message, cancellationToken);
        }
        finally
        {
            await scope.DisposeAsync();
        }
    }

    private async Task<OutboxMessageStatus?> SaveAsync(
        IServiceProvider services,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            services.GetRequiredService<IOutboxMessageRepository>().Update(message);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            return message.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApplicationConflictException)
        {
            // Row version moved since the claim: the immediate RSVP path reopened this row for a
            // newer request, and it will push the current state itself or leave a pending row.
            logger.LogInformation(
                "Outbox row was taken over by another writer while it was being processed; skipping settle. MessageType: {MessageType}",
                message.MessageType);
            return null;
        }
        catch (Exception exception)
        {
            // The lock expires on its own and the row is reclaimed by a later run; the handler's
            // work is idempotent so re-running it is safe.
            logger.LogWarning(
                "Outbox row could not be settled and will be reclaimed after its lock expires. MessageType: {MessageType}, ExceptionType: {ExceptionType}",
                message.MessageType,
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>Applies the handler's disposition to the row. Exposed for tests; pure.</summary>
    public static void Settle(OutboxMessage message, OutboxHandlingResult result, OutboxOptions settings, DateTime nowUtc)
    {
        switch (result.Disposition)
        {
            case OutboxHandlingDisposition.Completed:
                message.Status = OutboxMessageStatus.Processed;
                message.ProcessedAtUtc = nowUtc;
                break;
            case OutboxHandlingDisposition.Fail:
                message.AttemptCount += 1;
                message.Status = OutboxMessageStatus.DeadLettered;
                message.DeadLetterReason = result.Code;
                break;
            default:
                message.AttemptCount += 1;
                if (message.AttemptCount >= settings.MaxAttempts)
                {
                    message.Status = OutboxMessageStatus.DeadLettered;
                    message.DeadLetterReason = $"{MaxAttemptsExceededPrefix}{result.Code}";
                }
                else
                {
                    message.Status = OutboxMessageStatus.RetryScheduled;
                    message.AvailableAtUtc = nowUtc.Add(BackoffFor(message.AttemptCount));
                }

                break;
        }

        message.LockToken = null;
        message.LockedUntilUtc = null;
    }

    /// <summary>Delay before the next attempt, given how many attempts have already been made.</summary>
    public static TimeSpan BackoffFor(int attemptCount) =>
        Backoff[Math.Clamp(attemptCount, 1, Backoff.Count) - 1];
}
