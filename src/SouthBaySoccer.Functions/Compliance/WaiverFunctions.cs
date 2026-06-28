using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Waivers;
using SouthBaySoccer.Contracts.Compliance;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Compliance;

public sealed class WaiverFunctions(
    GetCurrentWaiverQueryHandler getCurrentWaiverHandler,
    AcceptCurrentWaiverCommandHandler acceptCurrentWaiverHandler,
    GetMyWaiverEligibilityQueryHandler getMyWaiverEligibilityHandler)
{
    [Function(nameof(GetCurrentWaiver))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetCurrentWaiver(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "waivers/current")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var waiver = await getCurrentWaiverHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new WaiverDocumentResponse(
                waiver.WaiverDocumentId,
                waiver.Version,
                waiver.Title,
                waiver.ContentHash,
                waiver.PublishedAtUtc),
            cancellationToken);
    }

    [Function(nameof(AcceptCurrentWaiver))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> AcceptCurrentWaiver(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "waivers/accept")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var acceptance = await acceptCurrentWaiverHandler.HandleAsync(new AcceptCurrentWaiverCommand(), cancellationToken);
        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new WaiverAcceptanceResponse(
                acceptance.WaiverAcceptanceId,
                acceptance.WaiverDocumentId,
                acceptance.Version,
                acceptance.AcceptedAtUtc),
            cancellationToken);
    }

    [Function(nameof(GetMyWaiverEligibility))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyWaiverEligibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "waivers/eligibility/me")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var eligibility = await getMyWaiverEligibilityHandler.HandleAsync(cancellationToken);
        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new WaiverEligibilityResponse(
                eligibility.HasCurrentWaiver,
                eligibility.CurrentWaiverDocumentId,
                eligibility.CurrentVersion,
                eligibility.Reason),
            cancellationToken);
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
