using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Groups;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class GroupEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(GroupChatFunctions.GetAvailableGroups), "groups")]
    [InlineData(nameof(GroupChatFunctions.GetMyGroups), "players/me/groups")]
    [InlineData(nameof(GroupChatFunctions.LinkMyGroup), "players/me/groups/link")]
    public void GroupEndpoint_WhenMetadataResolved_RequiresAuthenticatedPlayer(
        string methodName,
        string expectedRoute)
    {
        var method = typeof(GroupChatFunctions).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint GroupChatFunctions.{methodName}.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy
            .Should().Be(AuthenticationPolicies.AuthenticatedPlayer);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(expectedRoute);
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
