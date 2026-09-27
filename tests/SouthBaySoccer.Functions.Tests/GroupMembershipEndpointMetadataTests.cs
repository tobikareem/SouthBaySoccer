using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Groups;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class GroupMembershipEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(GroupMembershipFunctions.GetGroupCatalog), "get", "groups/catalog", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.GetMyMemberships), "get", "players/me/memberships", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.RequestMemberships), "post", "players/me/memberships/requests", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.LeaveGroup), "delete", "players/me/memberships/{groupChatId:guid}", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.GetGroupMembers), "get", "groups/{groupChatId:guid}/members", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.ApproveGroupMember), "post", "groups/{groupChatId:guid}/members/{playerProfileId:guid}/approve", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.DeclineGroupMember), "post", "groups/{groupChatId:guid}/members/{playerProfileId:guid}/decline", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.RemoveGroupMember), "post", "groups/{groupChatId:guid}/members/{playerProfileId:guid}/remove", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(GroupMembershipFunctions.AddGroupMember), "post", "groups/{groupChatId:guid}/members", AuthenticationPolicies.IsSuperAdmin)]
    [InlineData(nameof(GroupMembershipFunctions.SetGroupAdmin), "put", "groups/{groupChatId:guid}/admins", AuthenticationPolicies.IsSuperAdmin)]
    [InlineData(nameof(GroupMembershipFunctions.SearchPlayers), "get", "players/search", AuthenticationPolicies.AuthenticatedPlayer)]
    public void MembershipEndpoint_WhenMetadataResolved_DeclaresExactlyOnePolicy(
        string methodName,
        string expectedMethod,
        string expectedRoute,
        string expectedPolicy)
    {
        var method = typeof(GroupMembershipFunctions).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint GroupMembershipFunctions.{methodName}.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull("membership endpoints are never anonymous");
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(expectedPolicy);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(expectedRoute);
        trigger.Methods.Should().Equal(expectedMethod);
    }

    [Fact]
    public void MembershipEndpoints_WhenEnumerated_AllCarryAccessMetadata()
    {
        var endpoints = typeof(GroupMembershipFunctions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .ToArray();

        endpoints.Should().HaveCount(11);
        endpoints.Should().OnlyContain(method =>
            method.GetCustomAttribute<RequirePolicyAttribute>() != null
            && method.GetCustomAttribute<AllowAnonymousAttribute>() == null);
    }

    [Fact]
    public void SuperAdminPolicy_WhenMappedFromRoles_GrantedOnlyToOwner()
    {
        Infrastructure.Authentication.AuthenticationPolicyMapper.FromRoles(["Owner"])
            .Should().Contain(AuthenticationPolicies.IsSuperAdmin)
            .And.Contain(AuthenticationPolicies.CanManageGroupMembers);
        Infrastructure.Authentication.AuthenticationPolicyMapper.FromRoles(["Admin", "GameAdmin", "Captain", "Player"])
            .Should().NotContain(AuthenticationPolicies.IsSuperAdmin)
            .And.NotContain(AuthenticationPolicies.CanManageGroupMembers);
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
