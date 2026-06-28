using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Compliance;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Profiles;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class ProfileAndWaiverEndpointMetadataTests
{
    [Theory]
    [InlineData(typeof(ProfileFunctions), nameof(ProfileFunctions.GetMyProfile), "profiles/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(ProfileFunctions), nameof(ProfileFunctions.UpdateMyProfile), "profiles/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(ProfileFunctions), nameof(ProfileFunctions.CreateGuestProfile), "profiles/guests", AuthenticationPolicies.CanManagePlayers)]
    [InlineData(typeof(ProfileFunctions), nameof(ProfileFunctions.CreateProfileMerge), "profiles/merges", AuthenticationPolicies.CanManagePlayers)]
    [InlineData(typeof(WaiverFunctions), nameof(WaiverFunctions.GetCurrentWaiver), "waivers/current", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(WaiverFunctions), nameof(WaiverFunctions.AcceptCurrentWaiver), "waivers/accept", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(WaiverFunctions), nameof(WaiverFunctions.GetMyWaiverEligibility), "waivers/eligibility/me", AuthenticationPolicies.AuthenticatedPlayer)]
    public void M4Endpoint_WhenMetadataResolved_RequiresExpectedPolicy(
        Type endpointType,
        string methodName,
        string expectedRoute,
        string expectedPolicy)
    {
        var method = endpointType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint {endpointType.Name}.{methodName}.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(expectedPolicy);
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
