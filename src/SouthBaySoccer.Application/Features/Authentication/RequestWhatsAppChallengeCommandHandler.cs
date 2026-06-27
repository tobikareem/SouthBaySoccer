using FluentValidation;

namespace SouthBaySoccer.Application.Features.Authentication;

/// <summary>
/// Handles WhatsApp/Pickup Pal sign-in challenge requests.
/// </summary>
public sealed class RequestWhatsAppChallengeCommandHandler
{
    private readonly IValidator<RequestWhatsAppChallengeCommand> validator;
    private readonly IWhatsAppChallengeService challengeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestWhatsAppChallengeCommandHandler"/> class.
    /// </summary>
    /// <param name="validator">The command validator.</param>
    /// <param name="challengeService">The WhatsApp challenge service.</param>
    public RequestWhatsAppChallengeCommandHandler(
        IValidator<RequestWhatsAppChallengeCommand> validator,
        IWhatsAppChallengeService challengeService)
    {
        this.validator = validator;
        this.challengeService = challengeService;
    }

    /// <summary>
    /// Validates and creates a WhatsApp sign-in challenge.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>Challenge metadata that is safe to return to the client.</returns>
    public async Task<RequestWhatsAppChallengeResult> HandleAsync(
        RequestWhatsAppChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var maskedPhoneNumber = PhoneNumberMasker.Mask(command.PhoneNumber);

        var issueResult = await challengeService.CreateChallengeAsync(
            new WhatsAppChallengeIssueRequest(
                command.PhoneNumber,
                maskedPhoneNumber,
                command.CallbackUri),
            cancellationToken);

        return new RequestWhatsAppChallengeResult(
            issueResult.ChallengeId,
            maskedPhoneNumber,
            issueResult.ExpiresAtUtc);
    }
}
