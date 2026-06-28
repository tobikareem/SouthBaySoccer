using SouthBaySoccer.Domain.Entities.Identity;

namespace SouthBaySoccer.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for player profile, emergency contact, and profile merge workflows.
/// </summary>
public interface IPlayerProfileRepository : IRepository<PlayerProfile>
{
    /// <summary>
    /// Finds a profile by its linked identity user id.
    /// </summary>
    Task<PlayerProfile?> FindByIdentityUserIdAsync(Guid identityUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a profile by id, including guest profiles.
    /// </summary>
    Task<PlayerProfile?> FindProfileAsync(Guid playerProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the active emergency contact for a profile.
    /// </summary>
    Task<EmergencyContact?> FindEmergencyContactAsync(Guid playerProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an emergency contact.
    /// </summary>
    Task AddEmergencyContactAsync(EmergencyContact emergencyContact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing emergency contact.
    /// </summary>
    void UpdateEmergencyContact(EmergencyContact emergencyContact);

    /// <summary>
    /// Adds a profile merge audit record.
    /// </summary>
    Task AddProfileMergeAsync(ProfileMerge profileMerge, CancellationToken cancellationToken = default);
}
