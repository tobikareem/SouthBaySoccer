using FluentValidation;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Payments;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Features.Payments;

public sealed class CreateSessionDropInCheckoutCommandHandler(
    ICurrentUser currentUser,
    IPlayerProfileRepository playerProfileRepository,
    ISessionRepository sessionRepository,
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IValidator<CreateSessionDropInCheckoutCommand> validator)
{
    public async Task<CheckoutSessionResultModel> HandleAsync(
        CreateSessionDropInCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        _ = await sessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Session was not found.");

        var customerReference = await paymentRepository.FindStripeCustomerReferenceAsync(profile.Id, cancellationToken)
            ?? throw new ApplicationConflictException("Stripe customer reference is required before checkout can be created.");

        var checkout = await paymentGateway.CreateCheckoutSessionAsync(
            new CheckoutSessionRequest(
                profile.Id,
                command.SessionId,
                customerReference.StripeCustomerId,
                Mode: "payment",
                PriceCode: "drop-in",
                command.SuccessPath,
                command.CancelPath),
            cancellationToken);

        return new CheckoutSessionResultModel(
            command.SessionId,
            checkout.CheckoutUrl,
            checkout.ProviderSessionId,
            checkout.ExpiresAtUtc);
    }
}

public sealed class GetMySessionPaymentEligibilityQueryHandler(
    ICurrentUser currentUser,
    SouthBaySoccer.Application.Abstractions.Time.IClock clock,
    IPlayerProfileRepository playerProfileRepository,
    IPaymentRepository paymentRepository)
{
    public async Task<PaymentEligibilityModel> HandleAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var identityUserId = currentUser.UserId ?? throw new ApplicationUnauthenticatedException();
        var profile = await playerProfileRepository.FindByIdentityUserIdAsync(identityUserId, cancellationToken)
            ?? throw new ApplicationNotFoundException("Player profile was not found.");
        var projection = await paymentRepository.GetEligibilityAsync(profile.Id, sessionId, clock.UtcNow, cancellationToken);
        var eligible = projection.HasActiveMembership || projection.HasSessionDropIn;

        return new PaymentEligibilityModel(
            sessionId,
            profile.Id,
            eligible,
            projection.HasActiveMembership,
            projection.HasSessionDropIn,
            projection.EligibleUntilUtc,
            eligible ? null : "Payment required.");
    }
}