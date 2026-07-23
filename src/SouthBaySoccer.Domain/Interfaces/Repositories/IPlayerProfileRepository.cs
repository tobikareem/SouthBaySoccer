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
    /// Finds a profile by its linked Pickup Pal user id.
    /// </summary>
    Task<PlayerProfile?> FindByPickupPalUserIdAsync(string pickupPalUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a profile by its phone number hash.
    /// </summary>
    Task<PlayerProfile?> FindByPhoneNumberHashAsync(string phoneNumberHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a profile by its WhatsApp identity hash.
    /// </summary>
    Task<PlayerProfile?> FindByWhatsAppJidHashAsync(string whatsAppJidHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a profile by id, including guest profiles.
    /// </summary>
    Task<PlayerProfile?> FindProfileAsync(Guid playerProfileId, CancellationToken cancellationToken = default);

    /// <summary>Lists a bounded set of profiles by id for server-side projections.</summary>
    Task<IReadOnlyList<PlayerProfile>> ListProfilesAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active player profiles for the public player directory.
    /// </summary>
    /// <summary>
    /// Finds a profile by normalized display name, but only when the name is unambiguous. Returns
    /// null when nobody or more than one person matches, so a shared nickname never silently links
    /// two different players together.
    /// </summary>
    Task<PlayerProfile?> FindSingleByNormalizedDisplayNameAsync(
        string normalizedDisplayName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlayerDirectoryReadModel>> ListDirectoryAsync(CancellationToken cancellationToken = default);

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

/// <summary>
/// Read model for the players directory projection. Deliberately omits the linked identity user id:
/// the directory is a public-facing listing and navigation between screens uses
/// <see cref="PlayerProfileId"/>, so the identity id has no reason to leave the repository.
/// </summary>
public sealed record PlayerDirectoryReadModel(
    Guid PlayerProfileId,
    string DisplayName,
    string PreferredPosition,
    bool IsGuest,
    int Matches);
