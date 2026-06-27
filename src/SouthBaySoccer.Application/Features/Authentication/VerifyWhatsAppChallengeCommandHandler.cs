using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Handles WhatsApp/Pickup Pal challenge verification and application token exchange.
/// </summary>
public sealed class VerifyWhatsAppChallengeCommandHandler
{
    private readonly IValidator<VerifyWhatsAppChallengeCommand> validator;
    private readonly IWhatsAppChallengeService challengeService;
    private readonly IWhatsAppIdentityResolver identityResolver;
    private readonly IAuthenticationTokenIssuer tokenIssuer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyWhatsAppChallengeCommandHandler"/> class.
    /// </summary>
    /// <param name="validator">The command validator.</param>
    /// <param name="challengeService">The WhatsApp challenge service.</param>
    /// <param name="identityResolver">The identity resolver.</param>
    /// <param name="tokenIssuer">The token issuer.</param>
    public VerifyWhatsAppChallengeCommandHandler(
        IValidator<VerifyWhatsAppChallengeCommand> validator,
        IWhatsAppChallengeService challengeService,
        IWhatsAppIdentityResolver identityResolver,
        IAuthenticationTokenIssuer tokenIssuer)
    {
        this.validator = validator;
        this.challengeService = challengeService;
        this.identityResolver = identityResolver;
        this.tokenIssuer = tokenIssuer;
    }

    /// <summary>
    /// Validates, verifies, and exchanges a WhatsApp challenge for application tokens.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The authenticated player and issued application token set.</returns>
    public async Task<VerifyWhatsAppChallengeResult> HandleAsync(
        VerifyWhatsAppChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var verifiedChallenge = await challengeService.VerifyChallengeAsync(
            new WhatsAppChallengeVerificationRequest(command.ChallengeToken, command.CallbackUri),
            cancellationToken);

        var identity = await identityResolver.FindByVerifiedPhoneNumberAsync(
            verifiedChallenge.PhoneNumber,
            cancellationToken);

        var maskedPhoneNumber = PhoneNumberMasker.Mask(verifiedChallenge.PhoneNumber);
        if (identity is null)
        {
            throw new WhatsAppIdentityNotFoundException(maskedPhoneNumber);
        }

        var tokens = await tokenIssuer.IssueTokensAsync(
            new AuthenticationTokenSubject(
                identity.IdentityUserId,
                identity.PlayerProfileId,
                identity.Roles),
            cancellationToken);

        return new VerifyWhatsAppChallengeResult(
            identity.IdentityUserId,
            identity.PlayerProfileId,
            maskedPhoneNumber,
            identity.Roles,
            tokens);
    }
}
