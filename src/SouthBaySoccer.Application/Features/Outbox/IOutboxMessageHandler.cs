using SouthBaySoccer.Domain.Entities.Operations;

namespace SouthBaySoccer.Application.Features.Outbox;

/// <summary>
/// Executes one claimed outbox message of a single <see cref="MessageType"/>. Handlers must be
/// idempotent: the processor may run the same row again after a crash, a lock expiry, or an
/// ambiguous failure. Handlers never save; the processor commits the row's new status together with
/// whatever the handler tracked.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>The <see cref="OutboxMessage.MessageType"/> this handler owns.</summary>
    string MessageType { get; }

    /// <summary>Executes the message and reports how the processor should settle the row.</summary>
    Task<OutboxHandlingResult> HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>How the processor should settle a row after a handler ran.</summary>
public enum OutboxHandlingDisposition
{
    /// <summary>The work is done; mark the row Processed.</summary>
    Completed,

    /// <summary>The work did not complete for a transient reason; reschedule with backoff (or dead-letter when the attempt budget is spent).</summary>
    Retry,

    /// <summary>The work can never complete; dead-letter the row now.</summary>
    Fail,
}

/// <summary>Result of one handler run. <see cref="Code"/> is a safe, short reason and never a payload.</summary>
public sealed record OutboxHandlingResult(OutboxHandlingDisposition Disposition, string? Code)
{
    /// <summary>The work is done.</summary>
    public static OutboxHandlingResult Completed() => new(OutboxHandlingDisposition.Completed, null);

    /// <summary>Retry later with the given reason code.</summary>
    public static OutboxHandlingResult Retry(string code) => new(OutboxHandlingDisposition.Retry, code);

    /// <summary>Dead-letter now with the given reason code.</summary>
    public static OutboxHandlingResult Fail(string code) => new(OutboxHandlingDisposition.Fail, code);
}
