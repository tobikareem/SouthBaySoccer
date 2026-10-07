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

    public async Task<IReadOnlyList<PlayerProfile>> ListAccountDeletedProfilesAsync(
        IReadOnlyCollection<string> pickupPalUserIds,
        IReadOnlyCollection<string> phoneNumberHashes,
        IReadOnlyCollection<string> whatsAppJidHashes,
        CancellationToken cancellationToken = default)
    {
        if (pickupPalUserIds.Count == 0 && phoneNumberHashes.Count == 0 && whatsAppJidHashes.Count == 0)
        {
            return [];
        }

        var userIds = pickupPalUserIds.ToArray();
        var phones = phoneNumberHashes.ToArray();
        var jids = whatsAppJidHashes.ToArray();
        // Account deletion anonymizes Identity atomically with the profile soft delete. A merged
        // profile alone is not a deletion marker. Keep this lookup scoped to the incoming batch.
        return await (from profile in dbContext.PlayerProfiles.IgnoreQueryFilters().AsNoTracking()
                      join user in dbContext.Users.AsNoTracking() on profile.IdentityUserId equals user.Id
                      where profile.IsDeleted && profile.IdentityUserId != null && user.PlayerProfileId == null
                          && user.UserName != null && user.UserName.StartsWith("deleted:")
                          && ((profile.PickupPalUserId != null && userIds.Contains(profile.PickupPalUserId))
                              || (profile.PhoneNumberHash != null && phones.Contains(profile.PhoneNumberHash))
                              || (profile.WhatsAppJidHash != null && jids.Contains(profile.WhatsAppJidHash)))
                      select profile).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerProfile>> ListByPickupPalUserIdsAsync(
        IReadOnlyCollection<string> pickupPalUserIds,
        CancellationToken cancellationToken = default)
    {
        if (pickupPalUserIds.Count == 0)
        {
            return [];
        }

        var idArray = pickupPalUserIds as string[] ?? pickupPalUserIds.ToArray();
        return await dbContext.PlayerProfiles
            .Where(x => x.PickupPalUserId != null && idArray.Contains(x.PickupPalUserId))
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerProfile>> ListByPhoneNumberHashesAsync(
        IReadOnlyCollection<string> phoneNumberHashes,
        CancellationToken cancellationToken = default)
    {
        if (phoneNumberHashes.Count == 0)
        {
            return [];
        }

        var hashArray = phoneNumberHashes as string[] ?? phoneNumberHashes.ToArray();
        return await dbContext.PlayerProfiles
            .Where(x => x.PhoneNumberHash != null && hashArray.Contains(x.PhoneNumberHash))
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerProfile>> ListByWhatsAppJidHashesAsync(
        IReadOnlyCollection<string> whatsAppJidHashes,
        CancellationToken cancellationToken = default)
    {
        if (whatsAppJidHashes.Count == 0)
        {
            return [];
        }

        var hashArray = whatsAppJidHashes as string[] ?? whatsAppJidHashes.ToArray();
        return await dbContext.PlayerProfiles
            .Where(x => x.WhatsAppJidHash != null && hashArray.Contains(x.WhatsAppJidHash))
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlayerProfile>> ListByNormalizedDisplayNamesAsync(
        IReadOnlyCollection<string> normalizedDisplayNames,
        CancellationToken cancellationToken = default)
    {
        if (normalizedDisplayNames.Count == 0)
        {
            return [];
        }

        var nameArray = normalizedDisplayNames as string[] ?? normalizedDisplayNames.ToArray();
        return await dbContext.PlayerProfiles
            .Where(x => nameArray.Contains(x.NormalizedDisplayName))
            .OrderBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PlayerProfile?> FindSingleByNormalizedDisplayNameAsync(
        string normalizedDisplayName,
        CancellationToken cancellationToken = default)
    {
        var matches = await dbContext.PlayerProfiles
            .Where(x => x.NormalizedDisplayName == normalizedDisplayName)
            .OrderBy(x => x.CreatedAt)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return matches.Length == 1 ? matches[0] : null;
    }

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

    public async Task<IReadOnlyList<PlayerProfile>> SearchByDisplayNameAsync(
        string normalizedFragment,
        int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.PlayerProfiles
            .AsNoTracking()
            .Where(x => x.NormalizedDisplayName.Contains(normalizedFragment))
            .OrderBy(x => x.NormalizedDisplayName)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    // Share the leaderboard's bulk aggregation; query count never grows with the number of players.
    public async Task<IReadOnlyList<PlayerDirectoryReadModel>> ListDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await dbContext.PlayerProfiles
            .Select(x => new { x.Id, x.DisplayName, x.PreferredPosition, x.IsGuest })
            .ToArrayAsync(cancellationToken);
        if (profiles.Length == 0)
        {
            return [];
        }

        var aggregates = (await new PlayerStatAggregateReader(dbContext).ListAsync(
            seasonId: null, playerProfileId: null, groupChatId: null, cancellationToken))
            .ToDictionary(row => row.PlayerProfileId);
        var allPlayers = profiles.Select(profile => aggregates.GetValueOrDefault(profile.Id)
            ?? new PlayerStatAggregate
            {
                PlayerProfileId = profile.Id,
                DisplayName = profile.DisplayName,
                PreferredPosition = profile.PreferredPosition,
                IsGuest = profile.IsGuest,
            });

        return LeaderboardProjection.OrderDirectory(allPlayers)
            .Select(row => new PlayerDirectoryReadModel(
                row.PlayerProfileId,
                row.DisplayName,
                row.PreferredPosition,
                row.IsGuest,
                row.Appearances))
            .ToArray();
    }

    public Task<EmergencyContact?> FindEmergencyContactAsync(Guid playerProfileId, CancellationToken cancellationToken = default) =>
        dbContext.EmergencyContacts.SingleOrDefaultAsync(x => x.PlayerProfileId == playerProfileId, cancellationToken);

    public async Task AddAsync(PlayerProfile entity, CancellationToken cancellationToken = default) =>
        await dbContext.PlayerProfiles.AddAsync(entity, cancellationToken);

    public async Task AddEmergencyContactAsync(EmergencyContact emergencyContact, CancellationToken cancellationToken = default) =>
        await dbContext.EmergencyContacts.AddAsync(emergencyContact, cancellationToken);

    public async Task AddProfileMergeAsync(ProfileMerge profileMerge, CancellationToken cancellationToken = default) =>
        await dbContext.ProfileMerges.AddAsync(profileMerge, cancellationToken);

    public void Update(PlayerProfile entity)
    {
        // Repository reads are tracked. Let EF update only fields the caller changed: marking the
        // whole row modified can overwrite a deletion committed after an import loaded this row.
        var entry = dbContext.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            dbContext.PlayerProfiles.Update(entity);
            // A detached ordinary edit is never an instruction to restore a deleted account.
            if (!entity.IsDeleted)
            {
                entry.Property(profile => profile.IsDeleted).IsModified = false;
            }
        }
    }

    public void UpdateEmergencyContact(EmergencyContact emergencyContact) =>
        dbContext.EmergencyContacts.Update(emergencyContact);

    public void SoftDelete(PlayerProfile entity)
    {
        entity.IsDeleted = true;
        dbContext.PlayerProfiles.Update(entity);
    }
}
