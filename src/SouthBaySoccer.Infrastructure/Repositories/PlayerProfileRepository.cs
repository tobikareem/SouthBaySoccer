using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Repositories;

internal sealed class PlayerProfileRepository(SouthBaySoccerDbContext dbContext) : IPlayerProfileRepository
{
    public Task<PlayerProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PlayerProfile?> FindByIdentityUserIdAsync(Guid identityUserId, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles.SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId, cancellationToken);

    public Task<PlayerProfile?> FindByPickupPalUserIdAsync(string pickupPalUserId, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles.SingleOrDefaultAsync(x => x.PickupPalUserId == pickupPalUserId, cancellationToken);

    // FirstOrDefault rather than SingleOrDefault: phone and WhatsApp hashes are dedup hints without
    // unique indexes, so a pathological duplicate must not break the import.
    public Task<PlayerProfile?> FindByPhoneNumberHashAsync(string phoneNumberHash, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.PhoneNumberHash == phoneNumberHash, cancellationToken);

    public Task<PlayerProfile?> FindByWhatsAppJidHashAsync(string whatsAppJidHash, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.WhatsAppJidHash == whatsAppJidHash, cancellationToken);

    public Task<PlayerProfile?> FindProfileAsync(Guid playerProfileId, CancellationToken cancellationToken = default) =>
        dbContext.PlayerProfiles.SingleOrDefaultAsync(x => x.Id == playerProfileId, cancellationToken);

    public async Task<IReadOnlyList<PlayerProfile>> ListProfilesAsync(
        IReadOnlyCollection<Guid> playerProfileIds,
        CancellationToken cancellationToken = default) =>
        await dbContext.PlayerProfiles
            .Where(x => playerProfileIds.Contains(x.Id))
            .OrderBy(x => x.NormalizedDisplayName)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<PlayerDirectoryReadModel>> ListDirectoryAsync(CancellationToken cancellationToken = default) =>
        await (
            from profile in dbContext.PlayerProfiles
            let matches = dbContext.PlayerMatchStats
                .Where(stat => stat.PlayerProfileId == profile.Id)
                .Select(stat => stat.MatchId)
                .Distinct()
                .Count()
            orderby profile.NormalizedDisplayName, profile.DisplayName
            select new PlayerDirectoryReadModel(
                profile.Id,
                profile.DisplayName,
                profile.PreferredPosition,
                profile.IsGuest,
                matches))
        .ToArrayAsync(cancellationToken);

    public Task<EmergencyContact?> FindEmergencyContactAsync(Guid playerProfileId, CancellationToken cancellationToken = default) =>
        dbContext.EmergencyContacts.SingleOrDefaultAsync(x => x.PlayerProfileId == playerProfileId, cancellationToken);

    public async Task AddAsync(PlayerProfile entity, CancellationToken cancellationToken = default) =>
        await dbContext.PlayerProfiles.AddAsync(entity, cancellationToken);

    public async Task AddEmergencyContactAsync(EmergencyContact emergencyContact, CancellationToken cancellationToken = default) =>
        await dbContext.EmergencyContacts.AddAsync(emergencyContact, cancellationToken);

    public async Task AddProfileMergeAsync(ProfileMerge profileMerge, CancellationToken cancellationToken = default) =>
        await dbContext.ProfileMerges.AddAsync(profileMerge, cancellationToken);

    public void Update(PlayerProfile entity) =>
        dbContext.PlayerProfiles.Update(entity);

    public void UpdateEmergencyContact(EmergencyContact emergencyContact) =>
        dbContext.EmergencyContacts.Update(emergencyContact);

    public void SoftDelete(PlayerProfile entity)
    {
        entity.IsDeleted = true;
        dbContext.PlayerProfiles.Update(entity);
    }
}
