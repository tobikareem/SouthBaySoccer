using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// Resolves verified WhatsApp phone numbers to current player identities.
/// </summary>
public sealed class WhatsAppIdentityResolver(SouthBaySoccerDbContext dbContext) : IWhatsAppIdentityResolver
{
    public Task<WhatsAppIdentity?> FindByVerifiedPhoneNumberHashAsync(
        string phoneNumberHash,
        CancellationToken cancellationToken = default)
    {
        return FindByProfileQueryAsync(
            dbContext.PlayerProfiles.Where(profile => profile.PhoneNumberHash == phoneNumberHash),
            cancellationToken);
    }

    public Task<WhatsAppIdentity?> FindByIdentityUserIdAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default)
    {
        return FindByProfileQueryAsync(
            dbContext.PlayerProfiles.Where(profile => profile.IdentityUserId == identityUserId),
            cancellationToken);
    }

    private static async Task<WhatsAppIdentity?> FindByProfileQueryAsync(
        IQueryable<PlayerProfile> query,
        CancellationToken cancellationToken)
    {
        var profile = await query
            .Where(candidate => candidate.IdentityUserId != null && !candidate.IsGuest)
            .SingleOrDefaultAsync(cancellationToken);

        if (profile?.IdentityUserId is null)
        {
            return null;
        }

        var roles = new[] { profile.Role.ToString() };
        return new WhatsAppIdentity(
            profile.IdentityUserId.Value,
            profile.Id,
            profile.MaskedPhoneNumber ?? "***",
            roles);
    }
}