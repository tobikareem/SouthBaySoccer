using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SouthBaySoccer.Application.Features.Authentication;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Infrastructure.Identity;
using SouthBaySoccer.Infrastructure.Persistence;

namespace SouthBaySoccer.Infrastructure.Authentication;

/// <summary>
/// EF/Identity-backed Pickup Pal user synchronization.
/// </summary>
public sealed class PickupPalUserSyncService(
    SouthBaySoccerDbContext dbContext,
    UserManager<ApplicationIdentityUser> userManager,
    IConfiguredAdminPhoneNumberService configuredAdminPhoneNumberService) : IPickupPalUserSyncService
{
    public async Task<AuthenticationTokenSubject> SyncAsync(
        PickupPalUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Id);

        var pickupPalUserId = user.Id.Trim();
        var profile = await dbContext.PlayerProfiles
            .SingleOrDefaultAsync(x => x.PickupPalUserId == pickupPalUserId, cancellationToken);
        var phoneNumber = NormalizePhone(user.PhoneNumber);
        var displayName = BuildDisplayName(user);

        ApplicationIdentityUser identityUser;
        bool identityUserExists;
        if (profile?.IdentityUserId is { } existingIdentityUserId)
        {
            identityUser = await userManager.FindByIdAsync(existingIdentityUserId.ToString("D"))
                ?? throw new InvalidOperationException("Linked identity user was not found.");
            identityUserExists = true;
        }
        else
        {
            var foundIdentityUser = await userManager.FindByNameAsync(ToUserName(pickupPalUserId));
            identityUserExists = foundIdentityUser is not null;
            identityUser = foundIdentityUser ?? new ApplicationIdentityUser
                {
                    Id = Guid.NewGuid(),
                    UserName = ToUserName(pickupPalUserId),
                    EmailConfirmed = !string.IsNullOrWhiteSpace(user.Email),
                };
        }

        ApplyEmail(identityUser, user.Email);

        if (!identityUserExists)
        {
            if (profile is null)
            {
                profile = new PlayerProfile
                {
                    Id = Guid.NewGuid(),
                    PreferredPosition = string.Empty,
                    Role = PlayerRole.Player,
                };
                await dbContext.PlayerProfiles.AddAsync(profile, cancellationToken);
            }

            profile.IdentityUserId = identityUser.Id;
            profile.PickupPalUserId = pickupPalUserId;
            ApplyProfile(profile, displayName, phoneNumber, user.ProfilePicture, user.PreferredPositions);
            ApplyConfiguredAdminRole(profile, phoneNumber);
            identityUser.PlayerProfileId = profile.Id;

            var createResult = await userManager.CreateAsync(identityUser);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(ToIdentityErrorMessage(createResult));
            }
        }
        else
        {
            if (profile is null)
            {
                profile = new PlayerProfile
                {
                    Id = Guid.NewGuid(),
                    IdentityUserId = identityUser.Id,
                    PickupPalUserId = pickupPalUserId,
                    PreferredPosition = string.Empty,
                    Role = PlayerRole.Player,
                };
                await dbContext.PlayerProfiles.AddAsync(profile, cancellationToken);
            }

            profile.IdentityUserId = identityUser.Id;
            profile.PickupPalUserId = pickupPalUserId;
            ApplyProfile(profile, displayName, phoneNumber, user.ProfilePicture, user.PreferredPositions);
            ApplyConfiguredAdminRole(profile, phoneNumber);
            identityUser.PlayerProfileId = profile.Id;

            var updateResult = await userManager.UpdateAsync(identityUser);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(ToIdentityErrorMessage(updateResult));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthenticationTokenSubject(
            identityUser.Id,
            profile.Id,
            new[] { profile.Role.ToString() });
    }

    private static void ApplyEmail(ApplicationIdentityUser identityUser, string? email)
    {
        identityUser.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        identityUser.NormalizedEmail = identityUser.Email?.ToUpperInvariant();
        identityUser.EmailConfirmed = !string.IsNullOrWhiteSpace(identityUser.Email);
    }

    private void ApplyConfiguredAdminRole(PlayerProfile profile, string phoneNumber)
    {
        if (configuredAdminPhoneNumberService.IsConfiguredAdminPhoneNumber(phoneNumber) &&
            !IsAdministrativeRole(profile.Role))
        {
            profile.Role = PlayerRole.GameAdmin;
        }
    }

    private static bool IsAdministrativeRole(PlayerRole role) =>
        role is PlayerRole.Owner or PlayerRole.Admin or PlayerRole.GameAdmin;

    private static void ApplyProfile(
        PlayerProfile profile,
        string displayName,
        string phoneNumber,
        string? profilePicture,
        IReadOnlyList<string> preferredPositions)
    {
        profile.DisplayName = displayName;
        profile.NormalizedDisplayName = displayName.ToUpperInvariant();
        profile.PhoneNumberHash = AuthenticationHashing.Sha256(phoneNumber);
        profile.MaskedPhoneNumber = Mask(phoneNumber);
        profile.PhotoUri = string.IsNullOrWhiteSpace(profilePicture) ? null : profilePicture.Trim();
        profile.PreferredPosition = ToPreferredPosition(preferredPositions);
        profile.IsGuest = false;
    }

    private static string ToPreferredPosition(IReadOnlyList<string> preferredPositions)
    {
        var value = string.Join(
            ", ",
            preferredPositions
                .Where(position => !string.IsNullOrWhiteSpace(position))
                .Select(position => position.Trim()));

        return value.Length <= 64 ? value : value[..64];
    }

    private static string BuildDisplayName(PickupPalUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.NickName))
        {
            return user.NickName.Trim();
        }

        var fullName = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));

        return string.IsNullOrWhiteSpace(fullName) ? "Pickup Pal Player" : fullName;
    }

    private static string NormalizePhone(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return $"+{digits}";
    }

    private static string Mask(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "***" : $"+******{digits[^4..]}";
    }

    private static string ToUserName(string pickupPalUserId) => $"pickuppal:{pickupPalUserId}";

    private static string ToIdentityErrorMessage(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));
}

