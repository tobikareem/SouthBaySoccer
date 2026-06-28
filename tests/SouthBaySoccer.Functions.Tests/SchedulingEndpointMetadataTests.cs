using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Sessions;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class SchedulingEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(SchedulingFunctions.ListSeasons), "seasons", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.CreateSeason), "seasons", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.ListVenues), "venues", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.CreateVenue), "venues", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.ListUpcomingSessions), "sessions", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(nameof(SchedulingFunctions.CreateSession), "sessions", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.CancelSession), "sessions/{sessionId:guid}/cancel", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.CreateRecurrenceRule), "recurrence-rules", AuthenticationPolicies.CanManageSessions)]
    [InlineData(nameof(SchedulingFunctions.CreateSessionOccurrence), "sessions/occurrences", AuthenticationPolicies.CanManageSessions)]
    public void SchedulingEndpoint_WhenMetadataResolved_RequiresExpectedPolicy(
        string methodName,
        string expectedRoute,
        string expectedPolicy)
    {
        var method = typeof(SchedulingFunctions).GetMethod(methodName)
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
