using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Sessions;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class GameDayEndpointMetadataTests
{
    [Fact]
    public void GetTodayGameDayContext_WhenMetadataResolved_RequiresAuthenticatedPlayer()
    {
        var method = typeof(GameDayFunctions).GetMethod(nameof(GameDayFunctions.GetTodayGameDayContext))
            ?? throw new InvalidOperationException("Missing Game Day endpoint.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy
            .Should().Be(AuthenticationPolicies.AuthenticatedPlayer);
        var trigger = method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
            ?? throw new InvalidOperationException("Missing Game Day HTTP trigger metadata.");
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be("game-day/today");
        trigger.Methods.Should().ContainSingle().Which.Should().Be("get");
    }

    [Theory]
    [InlineData(nameof(GameDayFunctions.GetRecentGames), AuthenticationPolicies.CanManageSessions, "get", "game-day/recent")]
    [InlineData(nameof(GameDayFunctions.GetCaptainAssignment), AuthenticationPolicies.CanManageSessions, "get", "game-day/sessions/{sessionId:guid}/captains")]
    [InlineData(nameof(GameDayFunctions.AssignCaptains), AuthenticationPolicies.CanManageSessions, "put", "game-day/sessions/{sessionId:guid}/captains")]
    [InlineData(nameof(GameDayFunctions.GetTeamDraft), AuthenticationPolicies.AuthenticatedPlayer, "get", "game-day/sessions/{sessionId:guid}/draft")]
    [InlineData(nameof(GameDayFunctions.SaveTeamPicks), AuthenticationPolicies.AuthenticatedPlayer, "put", "game-day/sessions/{sessionId:guid}/teams/{teamId:guid}/picks")]
    [InlineData(nameof(GameDayFunctions.LockSessionTeams), AuthenticationPolicies.CanManageSessions, "post", "game-day/sessions/{sessionId:guid}/teams/lock")]
    [InlineData(nameof(GameDayFunctions.GetPostGameApproval), AuthenticationPolicies.AuthenticatedPlayer, "get", "game-day/sessions/{sessionId:guid}/post-game")]
    [InlineData(nameof(GameDayFunctions.ApprovePostGameStat), AuthenticationPolicies.AuthenticatedPlayer, "post", "game-day/sessions/{sessionId:guid}/post-game/events/{matchEventId:guid}/approve")]
    [InlineData(nameof(GameDayFunctions.SavePostGameTeamResult), AuthenticationPolicies.AuthenticatedPlayer, "put", "game-day/sessions/{sessionId:guid}/post-game/results/{teamId:guid}")]
    [InlineData(nameof(GameDayFunctions.PublishPostGame), AuthenticationPolicies.AuthenticatedPlayer, "post", "game-day/sessions/{sessionId:guid}/post-game/publish")]
    public void GameDayWorkflowEndpoint_WhenMetadataResolved_UsesExpectedPolicyAndRoute(
        string methodName,
        string policy,
        string httpMethod,
        string route)
    {
        var method = typeof(GameDayFunctions).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing {methodName} endpoint.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(policy);
        var trigger = method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
            ?? throw new InvalidOperationException($"Missing {methodName} HTTP trigger metadata.");
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be(route);
        trigger.Methods.Should().ContainSingle().Which.Should().Be(httpMethod);
    }
}
