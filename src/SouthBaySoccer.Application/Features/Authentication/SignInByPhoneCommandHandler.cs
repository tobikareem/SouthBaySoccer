using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Handles Pickup Pal phone lookup sign-in and SouthBaySoccer token issuance.
/// </summary>
public sealed class SignInByPhoneCommandHandler(
    IValidator<SignInByPhoneCommand> validator,
    IPickupPalUserClient pickupPalUserClient,
    IPickupPalUserSyncService pickupPalUserSyncService,
    IAuthenticationTokenIssuer tokenIssuer)
{
    /// <summary>
    /// Signs in a Pickup Pal user by phone and returns SouthBaySoccer session tokens.
    /// </summary>
    public async Task<AuthenticationTokenSet> HandleAsync(
        SignInByPhoneCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var phoneNumberDigits = SignInByPhoneCommandValidator.NormalizeDigits(command.PhoneNumber);
        var pickupPalUser = await pickupPalUserClient.FindByPhoneAsync(phoneNumberDigits, cancellationToken)
            ?? throw new PickupPalUserNotFoundException();

        var subject = await pickupPalUserSyncService.SyncAsync(pickupPalUser, cancellationToken);
        return await tokenIssuer.IssueTokensAsync(subject, cancellationToken);
    }
}
