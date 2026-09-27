using SouthBaySoccer.Domain.Entities.Operations;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>Persists immutable application audit entries.</summary>
public interface IAuditLogRepository
{
    /// <summary>Adds an immutable audit entry to the current unit of work.</summary>
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
