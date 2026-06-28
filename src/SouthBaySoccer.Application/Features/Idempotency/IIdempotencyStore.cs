namespace SouthBaySoccer.Application.Features.Idempotency;

public sealed record IdempotencyRecordModel(
    Guid Id,
    string RequestHash,
    int? ResponseStatusCode,
    string? ResponseBodyJson,
    DateTime? CompletedAtUtc);

public interface IIdempotencyStore
{
    Task<IdempotencyRecordModel?> FindAsync(
        Guid? identityUserId,
        string operationName,
        string key,
        CancellationToken cancellationToken = default);

    Task<IdempotencyRecordModel> CreateAsync(
        Guid? identityUserId,
        Guid? playerProfileId,
        string operationName,
        string key,
        string requestHash,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid id,
        int responseStatusCode,
        string responseBodyJson,
        string responseBodyHash,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default);

    Task AbandonAsync(
        Guid id,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}