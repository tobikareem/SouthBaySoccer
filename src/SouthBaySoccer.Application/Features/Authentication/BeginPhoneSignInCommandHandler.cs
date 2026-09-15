using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Entities.Operations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Handles Pickup Pal phone lookup sign-in. When WhatsApp verification is required the handler
/// records a <see cref="PendingPhoneSignIn"/> and issues no tokens; the sign-in completes through
/// <see cref="CompleteWhatsAppLoginCommandHandler"/>.
/// </summary>
public sealed class BeginPhoneSignInCommandHandler(
    IValidator<BeginPhoneSignInCommand> validator,
    IPickupPalUserClient pickupPalUserClient,
    IPickupPalUserSyncService pickupPalUserSyncService,
    IAuthenticationTokenIssuer tokenIssuer,
    IPendingPhoneSignInRepository pendingSignInRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOnboardingPolicy onboardingPolicy)
{
    /// <summary>Starts sign-in for the submitted phone number.</summary>
    /// <exception cref="PickupPalUserNotFoundException">Pickup Pal has no account for the number.</exception>
    public async Task<PhoneSignInStart> HandleAsync(
        BeginPhoneSignInCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var phoneNumberDigits = BeginPhoneSignInCommandValidator.NormalizeDigits(command.PhoneNumber);
        var pickupPalUser = await pickupPalUserClient.FindByPhoneAsync(phoneNumberDigits, cancellationToken)
            ?? throw new PickupPalUserNotFoundException();

        // The matched account's number is what the pending sign-in and the local profile are keyed
        // on, so hash what Pickup Pal returned rather than what the player typed.
        var matchedDigits = new string(pickupPalUser.PhoneNumber.Where(char.IsDigit).ToArray());
        if (!onboardingPolicy.RequireWhatsAppVerification ||
            onboardingPolicy.IsVerificationExempt(OnboardingPhone.ToE164(matchedDigits)))
        {
            var subject = await pickupPalUserSyncService.SyncAsync(pickupPalUser, cancellationToken);
            var tokens = await tokenIssuer.IssueTokensAsync(subject, cancellationToken);
            return new PhoneSignInStart(false, null, null, tokens);
        }

        var now = clock.UtcNow;
        await pendingSignInRepository.AddAsync(
            new PendingPhoneSignIn
            {
                Id = Guid.NewGuid(),
                PickupPalUserId = pickupPalUser.Id,
                PhoneNumberHash = OnboardingPhone.Hash(matchedDigits),
                ExpiresAtUtc = now.Add(onboardingPolicy.PendingSignInLifetime),
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PhoneSignInStart(
            true,
            OnboardingPhone.Mask(matchedDigits),
            BuildDisplayName(pickupPalUser),
            null);
    }

    private static string BuildDisplayName(PickupPalUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.NickName))
        {
            return user.NickName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            return user.FirstName.Trim();
        }

        return "there";
    }
}
