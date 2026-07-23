using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class AuditLogRepository(SouthBaySoccerDbContext dbContext) : IAuditLogRepository
{
    public Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) =>
        dbContext.AuditLogEntries.AddAsync(entry, cancellationToken).AsTask();
}
