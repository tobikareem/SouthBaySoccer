using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Application.Features.Groups;
using SouthBaySoccer.Contracts.Groups;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Groups;

/// <summary>
/// HTTP endpoints for group membership with approval (GRP-1). Membership lives in our own
/// database only; Pickup Pal is read (the <c>/linked</c> cross-check) and never written. Group
/// admin rights are per group, so the group-admin endpoints declare the authenticated-player
/// policy and the handler decides; super-admin-only endpoints fail closed on the pipeline policy
/// and the handler checks again.
/// </summary>
public sealed class GroupMembershipFunctions(
    GetGroupCatalogQueryHandler getGroupCatalogHandler,
    GetMyGroupMembershipsQueryHandler getMyMembershipsHandler,
    RequestGroupMembershipsCommandHandler requestMembershipsHandler,
    LeaveGroupCommandHandler leaveGroupHandler,
    GetGroupMembersQueryHandler getGroupMembersHandler,
    ReviewGroupMemberCommandHandler reviewGroupMemberHandler,
    AddGroupMemberCommandHandler addGroupMemberHandler,
    SetGroupAdminCommandHandler setGroupAdminHandler,
    SearchPlayersQueryHandler searchPlayersHandler)
{
    [Function(nameof(GetGroupCatalog))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetGroupCatalog(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "groups/catalog")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getGroupCatalogHandler.HandleAsync(new GetGroupCatalogQuery(), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetMyMemberships))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetMyMemberships(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/me/memberships")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await getMyMembershipsHandler.HandleAsync(new GetMyGroupMembershipsQuery(), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(RequestMemberships))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> RequestMemberships(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "players/me/memberships/requests")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<RequestGroupMembershipsRequest>(request, cancellationToken);
        var result = await requestMembershipsHandler.HandleAsync(
            new RequestGroupMembershipsCommand(body.GroupChatIds ?? []),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(LeaveGroup))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> LeaveGroup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "players/me/memberships/{groupChatId:guid}")] HttpRequestData request,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        var result = await leaveGroupHandler.HandleAsync(new LeaveGroupCommand(groupChatId), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(GetGroupMembers))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> GetGroupMembers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "groups/{groupChatId:guid}/members")] HttpRequestData request,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        var result = await getGroupMembersHandler.HandleAsync(new GetGroupMembersQuery(groupChatId), cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(ApproveGroupMember))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public Task<HttpResponseData> ApproveGroupMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupChatId:guid}/members/{playerProfileId:guid}/approve")] HttpRequestData request,
        Guid groupChatId,
        Guid playerProfileId,
        CancellationToken cancellationToken) =>
        ReviewAsync(request, groupChatId, playerProfileId, GroupMemberReview.Approve, cancellationToken);

    [Function(nameof(DeclineGroupMember))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public Task<HttpResponseData> DeclineGroupMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupChatId:guid}/members/{playerProfileId:guid}/decline")] HttpRequestData request,
        Guid groupChatId,
        Guid playerProfileId,
        CancellationToken cancellationToken) =>
        ReviewAsync(request, groupChatId, playerProfileId, GroupMemberReview.Decline, cancellationToken);

    [Function(nameof(RemoveGroupMember))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public Task<HttpResponseData> RemoveGroupMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupChatId:guid}/members/{playerProfileId:guid}/remove")] HttpRequestData request,
        Guid groupChatId,
        Guid playerProfileId,
        CancellationToken cancellationToken) =>
        ReviewAsync(request, groupChatId, playerProfileId, GroupMemberReview.Remove, cancellationToken);

    [Function(nameof(AddGroupMember))]
    [RequirePolicy(AuthenticationPolicies.IsSuperAdmin)]
    public async Task<HttpResponseData> AddGroupMember(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "groups/{groupChatId:guid}/members")] HttpRequestData request,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<AddGroupMemberRequest>(request, cancellationToken);
        var result = await addGroupMemberHandler.HandleAsync(
            new AddGroupMemberCommand(groupChatId, body.PlayerProfileId),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    [Function(nameof(SetGroupAdmin))]
    [RequirePolicy(AuthenticationPolicies.IsSuperAdmin)]
    public async Task<HttpResponseData> SetGroupAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "groups/{groupChatId:guid}/admins")] HttpRequestData request,
        Guid groupChatId,
        CancellationToken cancellationToken)
    {
        var body = await ReadRequiredJsonAsync<SetGroupAdminRequest>(request, cancellationToken);
        var result = await setGroupAdminHandler.HandleAsync(
            new SetGroupAdminCommand(groupChatId, body.PlayerProfileId, body.IsAdmin),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    /// <summary>
    /// <c>q</c> is a display-name fragment only; the validator refuses anything that looks like a
    /// phone number or an email so personal identifiers never travel in the query string.
    /// </summary>
    [Function(nameof(SearchPlayers))]
    [RequirePolicy(AuthenticationPolicies.AuthenticatedPlayer)]
    public async Task<HttpResponseData> SearchPlayers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "players/search")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = HttpUtility.ParseQueryString(request.Url.Query)["q"] ?? string.Empty;
        var result = await searchPlayersHandler.HandleAsync(new SearchPlayersQuery(query), cancellationToken);
        return await WriteJsonAsync(
            request,
            HttpStatusCode.OK,
            new PlayerSearchResponse(result
                .Select(player => new PlayerSearchResultDto(player.PlayerProfileId, player.DisplayName, player.Initials, player.MaskedPhone))
                .ToArray()),
            cancellationToken);
    }

    private async Task<HttpResponseData> ReviewAsync(
        HttpRequestData request,
        Guid groupChatId,
        Guid playerProfileId,
        GroupMemberReview review,
        CancellationToken cancellationToken)
    {
        var result = await reviewGroupMemberHandler.HandleAsync(
            new ReviewGroupMemberCommand(groupChatId, playerProfileId, review),
            cancellationToken);
        return await WriteJsonAsync(request, HttpStatusCode.OK, ToResponse(result), cancellationToken);
    }

    private static GroupCatalogResponse ToResponse(GroupCatalogModel model) =>
        new(model.Groups
            .Select(group => new GroupWithMembershipDto(
                group.GroupChatId,
                group.GroupName,
                group.MemberCount,
                group.Status?.ToString() ?? GroupMembershipStatuses.None,
                group.Role.ToString(),
                group.PendingRequestCount))
            .ToArray());

    private static MyGroupMembershipsResponse ToResponse(MyGroupMembershipsModel model) =>
        new(
            model.IsSuperAdmin,
            model.HasApprovedGroup,
            model.Memberships
                .Select(membership => new GroupMembershipDto(
                    membership.GroupChatId,
                    membership.GroupName,
                    membership.Status.ToString(),
                    membership.Role.ToString(),
                    membership.Source.ToString(),
                    membership.RequestedAtUtc,
                    membership.ApprovedAtUtc))
                .ToArray());

    private static GroupMembersResponse ToResponse(GroupMembersModel model) =>
        new(
            model.GroupChatId,
            model.GroupName,
            model.CanManageMembers,
            model.CanAppointAdmins,
            model.Pending.Select(ToDto).ToArray(),
            model.Members.Select(ToDto).ToArray());

    private static GroupMemberDto ToDto(GroupMemberModel member) =>
        new(
            member.PlayerProfileId,
            member.DisplayName,
            member.Initials,
            member.Status.ToString(),
            member.Role.ToString(),
            member.Source.ToString(),
            member.RequestedAtUtc,
            member.ApprovedAtUtc);

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
