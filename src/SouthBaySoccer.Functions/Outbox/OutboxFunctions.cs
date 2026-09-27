using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SouthBaySoccer.Functions.Outbox;

/// <summary>
/// Timer trigger that drains the transactional outbox (Pickup Pal roster syncs and account
/// deletions). Not an HTTP endpoint, so the HTTP pipeline middleware skips it and no access marker
/// applies. <c>Outbox:Enabled=false</c> turns the run into a no-op without touching the schedule.
/// </summary>
public sealed class OutboxFunctions(
    OutboxProcessor processor,
    IOptions<OutboxOptions> options,
    ILogger<OutboxFunctions> logger)
{
    [Function(nameof(ProcessOutbox))]
    public async Task ProcessOutbox(
        [TimerTrigger(OutboxOptions.Schedule)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("Outbox processing is disabled (Outbox:Enabled=false); skipping this run.");
            return;
        }

        var summary = await processor.RunAsync(cancellationToken);
        if (summary.Claimed > 0)
        {
            logger.LogInformation(
                "Outbox run claimed {Claimed} row(s): {Processed} processed, {Retried} rescheduled, {DeadLettered} dead-lettered.",
                summary.Claimed,
                summary.Processed,
                summary.Retried,
                summary.DeadLettered);
        }
    }
}
