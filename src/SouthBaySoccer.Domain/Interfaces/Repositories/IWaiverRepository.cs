using SouthBaySoccer.Domain.Entities.Compliance;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for waiver documents and waiver acceptances.
/// </summary>
public interface IWaiverRepository
{
    /// <summary>
    /// Finds the current published waiver document.
    /// </summary>
    Task<WaiverDocument?> GetCurrentPublishedWaiverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an existing acceptance for a profile and waiver document.
    /// </summary>
    Task<WaiverAcceptance?> FindAcceptanceAsync(
        Guid playerProfileId,
        Guid waiverDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the profile has accepted the current published waiver.
    /// </summary>
    Task<bool> HasCurrentAcceptanceAsync(Guid playerProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a waiver acceptance.
    /// </summary>
    Task AddAcceptanceAsync(WaiverAcceptance acceptance, CancellationToken cancellationToken = default);
}
