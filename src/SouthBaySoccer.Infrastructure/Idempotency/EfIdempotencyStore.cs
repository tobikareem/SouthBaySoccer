using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Idempotency;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Idempotency;

internal sealed class EfIdempotencyStore(SouthBaySoccerDbContext dbContext, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyRecordModel?> FindAsync(
        Guid? identityUserId,
        string operationName,
        string key,
        CancellationToken cancellationToken = default)
    {
        var record = await FindEntityAsync(identityUserId, operationName, key, cancellationToken);
        return record is null || record.ExpiresAtUtc <= clock.UtcNow ? null : ToModel(record);
    }

    public async Task<IdempotencyRecordModel> CreateAsync(
        Guid? identityUserId,
        Guid? playerProfileId,
        string operationName,
        string key,
        string requestHash,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindEntityAsync(identityUserId, operationName, key, cancellationToken);
        if (existing is not null)
        {
            if (existing.ExpiresAtUtc > clock.UtcNow)
            {
                throw new ApplicationConflictException("The idempotent request is already processing.");
            }

            existing.PlayerProfileId = playerProfileId;
            existing.RequestHash = requestHash;
            existing.ResponseStatusCode = null;
            existing.ResponseBodyJson = null;
            existing.ResponseBodyHash = null;
            existing.CompletedAtUtc = null;
            existing.ExpiresAtUtc = expiresAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToModel(existing);
        }

        var record = new IdempotencyKey
        {
            IdentityUserId = identityUserId,
            PlayerProfileId = playerProfileId,
            OperationName = operationName,
            Key = key,
            RequestHash = requestHash,
            ExpiresAtUtc = expiresAtUtc,
        };

        await dbContext.IdempotencyKeys.AddAsync(record, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToModel(record);
    }

    public async Task CompleteAsync(
        Guid id,
        int responseStatusCode,
        string responseBodyJson,
        string responseBodyHash,
        DateTime completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var record = await dbContext.IdempotencyKeys.SingleAsync(x => x.Id == id, cancellationToken);
        record.ResponseStatusCode = responseStatusCode;
        record.ResponseBodyJson = responseBodyJson;
        record.ResponseBodyHash = responseBodyHash;
        record.CompletedAtUtc = completedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(Guid id, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.IdempotencyKeys.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (record is null)
        {
            return;
        }

        record.ResponseStatusCode = null;
        record.ResponseBodyJson = null;
        record.ResponseBodyHash = null;
        record.CompletedAtUtc = null;
        record.ExpiresAtUtc = expiresAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<IdempotencyKey?> FindEntityAsync(
        Guid? identityUserId,
        string operationName,
        string key,
        CancellationToken cancellationToken) =>
        dbContext.IdempotencyKeys.SingleOrDefaultAsync(
            x => x.IdentityUserId == identityUserId
                && x.OperationName == operationName
                && x.Key == key,
            cancellationToken);

    private static IdempotencyRecordModel ToModel(IdempotencyKey record) =>
        new(
            record.Id,
            record.RequestHash,
            record.ResponseStatusCode,
            record.ResponseBodyJson,
            record.CompletedAtUtc);
}