using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication.Onboarding;

/// <summary>
/// EF/Identity-backed local account deletion. Soft-deletes the profile and its dependents, anonymizes
/// and locks the identity user so its email and Pickup Pal user name are free for re-registration,
/// and revokes every refresh token. Commits these changes with the audit/outbox intent.
/// <para>
/// The body performs several saves (Identity's <c>UpdateAsync</c> saves on its own, so does token
/// revocation), so it runs as one transaction inside the SQL execution strategy: with
/// <c>EnableRetryOnFailure</c> a bare <c>BeginTransactionAsync</c> throws, and the delegate must be
/// safe to re-run, hence the change-tracker reset on entry (see the 2026-07-21 lesson).
/// </para>
/// </summary>
public sealed class LocalAccountDeletionService(
    SouthBaySoccerDbContext dbContext,
    UserManager<ApplicationIdentityUser> userManager,
    IRefreshTokenRevocationService refreshTokenRevocationService) : ILocalAccountDeletionService
{
    private const string RevocationReason = "AccountDeleted";

    /// <inheritdoc />
    public async Task<LocalAccountDeletion> DeleteAsync(
        Guid identityUserId,
        Func<LocalAccountDeletion, CancellationToken, Task> recordDeletionAsync,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var deletion = await DeleteLocalRecordsAsync(identityUserId, cancellationToken);
            await AnonymizeIdentityUserAsync(identityUserId);
            await refreshTokenRevocationService.RevokeAllAsync(identityUserId, RevocationReason, cancellationToken);
            await recordDeletionAsync(deletion, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return deletion;
        });
    }

    private async Task<LocalAccountDeletion> DeleteLocalRecordsAsync(Guid identityUserId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.PlayerProfiles
            .SingleOrDefaultAsync(x => x.IdentityUserId == identityUserId, cancellationToken);
        if (profile is null)
        {
            // Preserve identifiers for a repeated request or an execution-strategy replay after
            // an ambiguous commit. The callback reuses the durable outbox idempotency key.
            return await dbContext.PlayerProfiles.IgnoreQueryFilters()
                .Where(x => x.IdentityUserId == identityUserId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new LocalAccountDeletion(x.Id, x.PickupPalUserId))
                .FirstOrDefaultAsync(cancellationToken) ?? new LocalAccountDeletion(null, null);
        }

        var emergencyContacts = await dbContext.EmergencyContacts
            .Where(x => x.PlayerProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        foreach (var contact in emergencyContacts)
        {
            contact.IsDeleted = true;
        }

        var groupLinks = await dbContext.PlayerGroupLinks
            .Where(x => x.PlayerProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        foreach (var link in groupLinks)
        {
            link.IsDeleted = true;
        }

        if (profile.PickupPalUserId is { } pickupPalUserId)
        {
            var registrations = await dbContext.PlayerRegistrations
                .Where(x => x.PickupPalUserId == pickupPalUserId)
                .ToListAsync(cancellationToken);
            foreach (var registration in registrations)
            {
                registration.IsDeleted = true;
            }
        }

        profile.IsDeleted = true;
        return new LocalAccountDeletion(profile.Id, profile.PickupPalUserId);
    }

    private async Task AnonymizeIdentityUserAsync(Guid identityUserId)
    {
        var identityUser = await userManager.FindByIdAsync(identityUserId.ToString("D"));
        if (identityUser is null)
        {
            return;
        }

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
}
