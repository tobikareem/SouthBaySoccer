using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Domain.Entities.Common;
using SouthBaySoccer.Infrastructure.Caching;

namespace SouthBaySoccer.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit fields and converts deletes for mutable entities into soft deletes.
/// </summary>
public sealed class AuditSoftDeleteSaveChangesInterceptor : SaveChangesInterceptor
{
    private const string SystemActor = "System";
    private readonly IClock clock;
    private readonly ICurrentUser currentUser;
    private readonly CacheEvictionQueue cacheEvictionQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditSoftDeleteSaveChangesInterceptor"/> class.
    /// </summary>
    /// <param name="clock">The application clock used for UTC audit timestamps.</param>
    /// <param name="currentUser">The ambient request principal used for audit actor stamps.</param>
    /// <param name="cacheEvictionQueue">Cache keys to evict once a save commits.</param>
    public AuditSoftDeleteSaveChangesInterceptor(
        IClock clock,
        ICurrentUser currentUser,
        CacheEvictionQueue cacheEvictionQueue)
    {
        this.clock = clock;
        this.currentUser = currentUser;
        this.cacheEvictionQueue = cacheEvictionQueue;
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        cacheEvictionQueue.Flush();
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    // Draining here rather than in UnitOfWork covers every commit path by construction. Several
    // services call dbContext.SaveChangesAsync directly, and any of them commits everything tracked
    // in the shared scoped context - so an enqueued key could otherwise go durable without eviction.
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        cacheEvictionQueue.Flush();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        StampAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAuditFields(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var actor = currentUser.UserId?.ToString("D") ?? SystemActor;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampCreated(entry, now, actor);
                    break;
                case EntityState.Modified:
                    StampUpdated(entry, now, actor);
                    break;
                case EntityState.Deleted when UsesSoftDelete(entry):
                    SoftDelete(entry, now, actor);
                    break;
                case EntityState.Deleted:
                    throw new InvalidOperationException(
                        $"Hard deletes are not allowed for {entry.Metadata.ClrType.Name}. Use a retention/purge service with an explicit policy.");
            }
        }
    }

    private static void StampCreated(EntityEntry<BaseEntity> entry, DateTime now, string actor)
    {
        entry.Entity.CreatedAt = now;
        entry.Entity.CreatedBy = actor;
        entry.Entity.UpdatedAt = null;
        entry.Entity.UpdatedBy = null;
        entry.Entity.IsDeleted = false;
    }

    private static void StampUpdated(EntityEntry<BaseEntity> entry, DateTime now, string actor)
    {
        entry.Entity.UpdatedAt = now;
        entry.Entity.UpdatedBy = actor;
        entry.Property(x => x.CreatedAt).IsModified = false;
        entry.Property(x => x.CreatedBy).IsModified = false;
    }

    private static void SoftDelete(EntityEntry<BaseEntity> entry, DateTime now, string actor)
    {
        entry.State = EntityState.Modified;
        entry.Entity.IsDeleted = true;
        StampUpdated(entry, now, actor);
    }

    private static bool UsesSoftDelete(EntityEntry<BaseEntity> entry)
    {
        return entry.Metadata.FindAnnotation(SouthBaySoccerEfAnnotations.UsesSoftDelete)?.Value as bool? == true;
    }
}
