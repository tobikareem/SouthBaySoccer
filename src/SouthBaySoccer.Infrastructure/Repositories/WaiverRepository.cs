using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Compliance;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class WaiverRepository(SouthBaySoccerDbContext dbContext) : IWaiverRepository
{
    public Task<WaiverDocument?> GetCurrentPublishedWaiverAsync(CancellationToken cancellationToken = default) =>
        dbContext.WaiverDocuments
            .SingleOrDefaultAsync(x => x.Status == WaiverDocumentStatus.Published, cancellationToken);

    public Task<WaiverAcceptance?> FindAcceptanceAsync(
        Guid playerProfileId,
        Guid waiverDocumentId,
        CancellationToken cancellationToken = default) =>
        dbContext.WaiverAcceptances.SingleOrDefaultAsync(
            x => x.PlayerProfileId == playerProfileId && x.WaiverDocumentId == waiverDocumentId,
            cancellationToken);

    public async Task<bool> HasCurrentAcceptanceAsync(Guid playerProfileId, CancellationToken cancellationToken = default)
    {
        var currentWaiverId = await dbContext.WaiverDocuments
            .Where(x => x.Status == WaiverDocumentStatus.Published)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return currentWaiverId is not null &&
            await dbContext.WaiverAcceptances.AnyAsync(
                x => x.PlayerProfileId == playerProfileId && x.WaiverDocumentId == currentWaiverId.Value,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListPlayerIdsWithCurrentAcceptanceAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        CancellationToken cancellationToken = default)
    {
        if (playerProfileIds.Count == 0)
        {
            return [];
        }

        var currentWaiverId = await dbContext.WaiverDocuments
            .Where(x => x.Status == WaiverDocumentStatus.Published)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentWaiverId is null)
        {
            return [];
        }

        var idArray = playerProfileIds as Guid[] ?? playerProfileIds.ToArray();
        return await dbContext.WaiverAcceptances
            .Where(x => x.WaiverDocumentId == currentWaiverId.Value && idArray.Contains(x.PlayerProfileId))
            .Select(x => x.PlayerProfileId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddAcceptanceAsync(WaiverAcceptance acceptance, CancellationToken cancellationToken = default) =>
        await dbContext.WaiverAcceptances.AddAsync(acceptance, cancellationToken);
}
