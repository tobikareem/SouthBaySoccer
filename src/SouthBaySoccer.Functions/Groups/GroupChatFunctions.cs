using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Groups;

/// <summary>
/// HTTP endpoints for WhatsApp group-chat linking. Reads from Pickup Pal are GET-only; the link is
/// persisted in our own database, which is the source of truth for player-to-group membership.
/// </summary>
public sealed class GroupChatFunctions(
    GetAvailableGroupsQueryHandler getAvailableGroupsHandler,
    GetMyGroupsQueryHandler getMyGroupsHandler,
    LinkPlayerToGroupCommandHandler linkPlayerToGroupHandler)
{
    [Function(nameof(GetAvailableGroups))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetAvailableGroups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "groups")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getAvailableGroupsHandler.HandleAsync(new GetAvailableGroupsQuery(), cancellationToken);
        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new AvailableGroupsResponse(result.Select(ToDto).ToArray()),
            cancellationToken);
    }

    [Function(nameof(GetMyGroups))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyGroups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/me/groups")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getMyGroupsHandler.HandleAsync(new GetMyGroupsQuery(), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(LinkMyGroup))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> LinkMyGroup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "players/me/groups/link")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<LinkGroupRequest>(request, cancellationToken);
        var result = await linkPlayerToGroupHandler.HandleAsync(
            new LinkPlayerToGroupCommand(body.GroupExternalId),
            cancellationToken);

        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    private static MyGroupsResponse ToResponse(MyGroupsResult result) =>
        new(result.IsLinked, result.Groups.Select(ToDto).ToArray());

    private static GroupChatDto ToDto(GroupSummary group) =>
        new(group.Id, group.ExternalId, group.GroupName, group.MemberCount, group.IsLinked, group.IsPrimary);

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
