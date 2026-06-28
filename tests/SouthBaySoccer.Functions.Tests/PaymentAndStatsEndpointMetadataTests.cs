using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Contracts.Payments;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Payments;
using SouthBaySoccer.Functions.Stats;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class PaymentAndStatsEndpointMetadataTests
{
    [Theory]
    [InlineData(typeof(PaymentFunctions), nameof(PaymentFunctions.CreateSessionDropInCheckout), "sessions/{sessionId:guid}/payments/drop-in-checkout", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(PaymentFunctions), nameof(PaymentFunctions.GetMySessionPaymentEligibility), "sessions/{sessionId:guid}/payments/eligibility/me", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.CreateMatch), "stats/matches", AuthenticationPolicies.CanAssignTeams)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.RecordMatchEvents), "stats/matches/{matchId:guid}/events", AuthenticationPolicies.CanRecordStats)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.ReviewMatchEvent), "stats/matches/{matchId:guid}/events/{matchEventId:guid}/review", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.ResolveMatchReview), "stats/matches/{matchId:guid}/review-resolution", AuthenticationPolicies.CanRecordStats)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.RecordMatchResults), "stats/matches/{matchId:guid}/results", AuthenticationPolicies.CanRecordStats)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.SubmitPeerFeedback), "stats/matches/{matchId:guid}/feedback", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.LockMatchStats), "stats/matches/{matchId:guid}/lock", AuthenticationPolicies.CanRecordStats)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.AddStatCorrection), "stats/matches/{matchId:guid}/corrections", AuthenticationPolicies.CanRecordStats)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.ReassignProfileStats), "stats/profile-merge/reassign", AuthenticationPolicies.CanManagePlayers)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.GetSeasonLeaderboard), "stats/leaderboards", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.GetPlayerStats), "players/{playerProfileId:guid}/stats", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.GetMyPlayerStats), "players/me/stats", AuthenticationPolicies.AuthenticatedPlayer)]
    [InlineData(typeof(StatsFunctions), nameof(StatsFunctions.GetPlayerRecentForm), "players/{playerProfileId:guid}/recent-form", AuthenticationPolicies.AuthenticatedPlayer)]
    public void Endpoint_WhenMetadataResolved_RequiresExpectedPolicy(Type functionType, string methodName, string expectedRoute, string expectedPolicy)
    {
        var method = functionType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing endpoint {methodName}.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(expectedPolicy);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(expectedRoute);
    }

    [Fact]
    public void Contracts_WhenReflected_ExposeDropInAndMvpAuthorities()
    {
        typeof(CreateDropInCheckoutRequest).GetProperty(nameof(CreateDropInCheckoutRequest.SuccessPath)).Should().NotBeNull();
        typeof(SubmitPeerFeedbackRequest).GetProperty(nameof(SubmitPeerFeedbackRequest.MvpPlayerProfileId)).Should().NotBeNull();
        typeof(MatchEventRequest).GetProperty(nameof(MatchEventRequest.AssistPlayerProfileId)).Should().NotBeNull();
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
