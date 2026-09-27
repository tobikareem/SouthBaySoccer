using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Announcements;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class AnnouncementEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(AnnouncementFunctions.GetGroupAnnouncements), "groups/{groupId:guid}/announcements")]
    [InlineData(nameof(AnnouncementFunctions.MarkGroupAnnouncementsRead), "groups/{groupId:guid}/announcements/read")]
    [InlineData(nameof(AnnouncementFunctions.GetUnreadAnnouncementCount), "players/me/announcements/unread-count")]
    public void PlayerAnnouncementEndpoint_WhenMetadataResolved_RequiresAuthenticatedPlayer(
        string methodName,
        string expectedRoute)
    {
        var method = GetEndpoint(methodName);

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy
            .Should().Be(AuthenticationPolicies.AuthenticatedPlayer);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(expectedRoute);
    }

    [Theory]
    [InlineData(nameof(AnnouncementFunctions.PostAnnouncement), "groups/{groupId:guid}/announcements")]
    [InlineData(nameof(AnnouncementFunctions.GetSentAnnouncements), "players/me/announcements/sent")]
    public void AdminAnnouncementEndpoint_WhenMetadataResolved_RequiresSessionManagementPolicy(
        string methodName,
        string expectedRoute)
    {
        var method = GetEndpoint(methodName);

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy
            .Should().Be(
                AuthenticationPolicies.CanManageSessions,
                because: "broadcasting and its read receipts are admin surfaces, not player surfaces");
        GetHttpTrigger(method).Route.Should().Be(expectedRoute);
    }

    [Fact]
    public void PostAnnouncement_WhenMetadataResolved_IsAPostSoARetryCannotBecomeASecondBroadcast()
    {
        var trigger = GetHttpTrigger(GetEndpoint(nameof(AnnouncementFunctions.PostAnnouncement)));

        trigger.Methods.Should().ContainSingle().Which.Should().Be("post");
    }

    private static MethodInfo GetEndpoint(string methodName) =>
        typeof(AnnouncementFunctions).GetMethod(methodName)
        ?? throw new InvalidOperationException($"Missing endpoint AnnouncementFunctions.{methodName}.");

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HttpTrigger on {method.Name}.");
}
