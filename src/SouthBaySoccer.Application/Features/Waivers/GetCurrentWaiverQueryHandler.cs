using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Waivers;

public sealed class GetCurrentWaiverQueryHandler(IWaiverRepository waiverRepository)
{
    public async Task<WaiverDocumentModel> HandleAsync(CancellationToken cancellationToken = default)
    {
        var waiver = await waiverRepository.GetCurrentPublishedWaiverAsync(cancellationToken)
            ?? throw new ApplicationNotFoundException("Current waiver document was not found.");

        return new WaiverDocumentModel(
            waiver.Id,
            waiver.Version,
            waiver.Title,
            waiver.ContentHash,
            waiver.PublishedAtUtc);
    }
}
