using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Payments;
using SouthBaySoccer.Contracts.Payments;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Payments;

public sealed class PaymentFunctions(
    CreateSessionDropInCheckoutCommandHandler createDropInCheckoutHandler,
    GetMySessionPaymentEligibilityQueryHandler getPaymentEligibilityHandler,
    IdempotentRequestExecutor idempotentRequestExecutor)
{
    [Function(nameof(CreateSessionDropInCheckout))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> CreateSessionDropInCheckout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sessions/{sessionId:guid}/payments/drop-in-checkout")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<CreateDropInCheckoutRequest>(request, cancellationToken);
        return await idempotentRequestExecutor.ExecuteAsync(
            request,
            nameof(CreateSessionDropInCheckout),
            GetIdempotencyKey(request),
            new { sessionId, body.SuccessPath, body.CancelPath },
            async token =>
            {
                var result = await createDropInCheckoutHandler.HandleAsync(
                    new CreateSessionDropInCheckoutCommand(sessionId, body.SuccessPath, body.CancelPath),
                    token);
                return new IdempotentResponse<CheckoutSessionResponse>(HttpStatusCode.Created, ToResponse(result));
            },
            cancellationToken);
    }

    [Function(nameof(GetMySessionPaymentEligibility))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMySessionPaymentEligibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sessions/{sessionId:guid}/payments/eligibility/me")] HttpRequestData request,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await getPaymentEligibilityHandler.HandleAsync(sessionId, cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    private static CheckoutSessionResponse ToResponse(CheckoutSessionResultModel result) =>
        new(result.SessionId, result.CheckoutUrl, result.ProviderSessionId, result.ExpiresAtUtc);

    private static PaymentEligibilityResponse ToResponse(PaymentEligibilityModel result) =>
        new(
            result.SessionId,
            result.PlayerProfileId,
            result.IsEligible,
            result.HasActiveMembership,
            result.HasSessionDropIn,
            result.EligibleUntilUtc,
            result.Reason);

    private static string GetIdempotencyKey(HttpRequestData request)
    {
        if (request.Headers.TryGetValues("Idempotency-Key", out var values))
        {
            var value = values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new ValidationProblemException(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["The Idempotency-Key header is required for this operation."],
        });
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<T>(cancellationToken);
        if (body is null)
        {
            throw new ValidationProblemException(new Dictionary<string, string[]>
            {
                ["body"] = ["A request body is required."],
            });
        }

        return body;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(
        HttpRequestData request,
        HttpStatusCode statusCode,
        T value,
        CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(value, cancellationToken: cancellationToken);
        return response;
    }
}