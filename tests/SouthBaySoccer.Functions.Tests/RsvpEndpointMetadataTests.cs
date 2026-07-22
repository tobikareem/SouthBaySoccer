using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Contracts.Rsvps;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Rsvps;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class RsvpEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(RsvpFunctions.SubmitRsvp), "sessions/{sessionId:guid}/rsvp", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(RsvpFunctions.CancelRsvp), "sessions/{sessionId:guid}/rsvp", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(RsvpFunctions.GetMyRsvp), "sessions/{sessionId:guid}/rsvp/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(RsvpFunctions.GetSessionRoster), "sessions/{sessionId:guid}/roster", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(RsvpFunctions.AdminOverrideRsvp), "sessions/{sessionId:guid}/rsvp/admin-override", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(RsvpFunctions.CheckInPlayer), "sessions/{sessionId:guid}/check-ins", AuthenticationPolicies.CanCheckInPlayers)]
    [InlineData(nameof(RsvpFunctions.RecordNoShows), "sessions/{sessionId:guid}/check-ins/no-shows", AuthenticationPolicies.CanCheckInPlayers)]
    public void RsvpEndpoint_WhenMetadataResolved_RequiresExpectedPolicy(
        string methodName,
        string expectedRoute,
        string expectedPolicy)
    {
        var method = typeof(RsvpFunctions).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint {methodName}.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(expectedPolicy);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(expectedRoute);
    }

    [Fact]
    public void CheckInContracts_WhenReflected_ExposeLateOverrideFields()
    {
        typeof(CheckInPlayerRequest).GetProperty(nameof(CheckInPlayerRequest.LateOverrideReason)).Should().NotBeNull();
        typeof(CheckInResponseDto).GetProperty(nameof(CheckInResponseDto.IsLateOverride)).Should().NotBeNull();
        typeof(CheckInResponseDto).GetProperty(nameof(CheckInResponseDto.AdminOverrideId)).Should().NotBeNull();
        typeof(CheckInResponseDto).GetProperty(nameof(CheckInResponseDto.LateOverrideReason)).Should().NotBeNull();
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
