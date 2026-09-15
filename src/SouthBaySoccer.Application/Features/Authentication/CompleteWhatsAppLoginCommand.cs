using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Onboarding;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>Completes a verified sign-in with the login token the Pickup Pal bot linked back with.</summary>
/// <param name="Token">The single-use login token from the app link.</param>
/// <param name="RememberDevice">Whether to issue a long-lived refresh token for this device.</param>
public sealed record CompleteWhatsAppLoginCommand(string Token, bool RememberDevice);

/// <summary>Validates <see cref="CompleteWhatsAppLoginCommand"/>.</summary>
public sealed class CompleteWhatsAppLoginCommandValidator : AbstractValidator<CompleteWhatsAppLoginCommand>
{
    /// <summary>Initializes a new instance of the <see cref="CompleteWhatsAppLoginCommandValidator"/> class.</summary>
    public CompleteWhatsAppLoginCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(256);
    }
}

/// <summary>
/// Handles <see cref="CompleteWhatsAppLoginCommand"/>: redeems the token with Pickup Pal, binds it to
/// the pending sign-in started for that user, syncs local identity, and issues tokens.
/// </summary>
public sealed class CompleteWhatsAppLoginCommandHandler(
    IValidator<CompleteWhatsAppLoginCommand> validator,
    IPickupPalOnboardingClient onboardingClient,
    IPendingPhoneSignInRepository pendingSignInRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPickupPalUserSyncService pickupPalUserSyncService,
    IAuthenticationTokenIssuer tokenIssuer,
    IOnboardingPolicy onboardingPolicy)
{
    /// <summary>Completes the sign-in and returns session tokens.</summary>
    /// <exception cref="OnboardingTokenException">
    /// The token is expired or invalid, or it resolved to a user with no active pending sign-in
    /// (a token for another account, or a sign-in that expired or was already completed).
    /// </exception>
    public async Task<AuthenticationTokenSet> HandleAsync(
        CompleteWhatsAppLoginCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        PickupPalUser user;
        try
        {
            user = await onboardingClient.RedeemLoginTokenAsync(command.Token.Trim(), cancellationToken);
        }
        catch (PickupPalOnboardingException exception)
        {
            throw exception.Failure == PickupPalOnboardingFailure.TokenExpired
                ? new OnboardingTokenException(OnboardingTokenFailure.Expired)
                : new OnboardingTokenException(OnboardingTokenFailure.Invalid);
        }

        var now = clock.UtcNow;
        // The client does not carry a pending-sign-in id, so the binding is the redeemed user: a
        // token for anyone without a live pending sign-in is a mismatch, and the response never says
        // whether that user exists. Every live row is consumed so nothing is left to replay against.
        var pendingSignIns = (await pendingSignInRepository.ListActiveByPickupPalUserIdAsync(user.Id, now, cancellationToken))
            .Where(pending => pending.IsActiveAt(now))
            .ToArray();
        if (pendingSignIns.Length == 0)
        {
            throw new OnboardingTokenException(OnboardingTokenFailure.Mismatch);
        }

        foreach (var pendingSignIn in pendingSignIns)
        {
            pendingSignIn.ConsumedAtUtc = now;
            pendingSignIn.RememberDevice = command.RememberDevice;
            pendingSignInRepository.Update(pendingSignIn);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConflictException)
        {
            // The row version changed under us: a concurrent completion already consumed this
            // sign-in (the same link opened twice). Only the first caller gets tokens.
            throw new OnboardingTokenException(OnboardingTokenFailure.Mismatch);
        }

        var subject = await pickupPalUserSyncService.SyncAsync(user, cancellationToken);

        // Both paths pass an explicit lifetime: a session sign-in must expire well before the
        // remembered one, otherwise "remember this device" would change nothing.
        var refreshTokenLifetime = command.RememberDevice
            ? onboardingPolicy.RememberDeviceRefreshTokenLifetime
            : onboardingPolicy.SessionRefreshTokenLifetime;
        return await tokenIssuer.IssueTokensAsync(subject, refreshTokenLifetime, cancellationToken);
    }
}
