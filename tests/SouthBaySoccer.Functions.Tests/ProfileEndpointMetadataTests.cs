using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Profiles;

namespace SouthBaySoccer.Functions.Tests;

public sealed class ProfileEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(ProfileFunctions.GetMyProfile), "profiles/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(ProfileFunctions.GetPlayerProfile), "profiles/{playerProfileId:guid}", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(ProfileFunctions.UpdateMyProfile), "profiles/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(ProfileFunctions.CreateGuestProfile), "profiles/guests", AuthenticationPolicies.CanManagePlayers)]
    [InlineData(nameof(ProfileFunctions.CreateProfileMerge), "profiles/merges", AuthenticationPolicies.CanManagePlayers)]
    public void ProfileEndpoint_WhenMetadataResolved_RequiresExpectedPolicy(
        string methodName,
        string expectedRoute,
        string expectedPolicy)
    {
        var method = typeof(ProfileFunctions).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint {methodName}.");

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
