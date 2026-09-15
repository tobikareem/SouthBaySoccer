using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication.Onboarding;

/// <summary>
/// EF/Identity-backed local account deletion. Soft-deletes the profile and its dependents (the audit
/// interceptor converts <c>Remove</c> into <c>IsDeleted = true</c>), anonymizes and locks the identity
/// user so its email and Pickup Pal user name are free for a future re-registration, and revokes every
/// refresh token. Nothing is hard-deleted.
/// </summary>
public sealed class LocalAccountDeletionService(
    SouthBaySoccerDbContext dbContext,
    UserManager<ApplicationIdentityUser> userManager,
    IRefreshTokenRevocationService refreshTokenRevocationService) : ILocalAccountDeletionService
{
    private const string RevocationReason = "AccountDeleted";

    public async Task<LocalAccountDeletion> DeleteAsync(Guid identityUserId, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.PlayerProfiles
            .SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId, cancellationToken);

        if (profile is not null)
        {
            var emergencyContacts = await dbContext.EmergencyContacts
                .Where(x => x.PlayerProfileId == profile.Id)
                .ToListAsync(cancellationToken);
            dbContext.EmergencyContacts.RemoveRange(emergencyContacts);

            var groupLinks = await dbContext.PlayerGroupLinks
                .Where(x => x.PlayerProfileId == profile.Id)
                .ToListAsync(cancellationToken);
            dbContext.PlayerGroupLinks.RemoveRange(groupLinks);

            if (profile.PickupPalUserId is { } pickupPalUserId)
            {
                var registrations = await dbContext.PlayerRegistrations
                    .Where(x => x.PickupPalUserId == pickupPalUserId)
                    .ToListAsync(cancellationToken);
                dbContext.PlayerRegistrations.RemoveRange(registrations);
            }

            dbContext.PlayerProfiles.Remove(profile);
        }

        var identityUser = await userManager.FindByIdAsync(identityUserId.ToString("D"));
        if (identityUser is not null)
        {
            // Identity requires a unique, non-empty email, so the real address is replaced with a
            // synthetic one that can never collide or receive mail.
            var placeholder = $"deleted:{identityUserId:N}";
            identityUser.UserName = placeholder;
            identityUser.NormalizedUserName = placeholder.ToUpperInvariant();
            identityUser.Email = $"{placeholder.Replace(':', '+')}@deleted.invalid";
            identityUser.NormalizedEmail = identityUser.Email.ToUpperInvariant();
            identityUser.EmailConfirmed = false;
            identityUser.PhoneNumber = null;
            identityUser.LockoutEnabled = true;
            identityUser.LockoutEnd = DateTimeOffset.MaxValue;
            identityUser.PlayerProfileId = null;

            var result = await userManager.UpdateAsync(identityUser);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        await refreshTokenRevocationService.RevokeAllAsync(identityUserId, RevocationReason, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LocalAccountDeletion(profile?.Id, profile?.PickupPalUserId);
    }
}
