using System.Reflection;
using System.Text.Json;
using Azure.Core.Serialization;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Contracts.GameDay;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Sessions;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class GameDayEndpointMetadataTests
{
    [Fact]
    public async Task GetRecentGameSummaries_WhenHistoryExists_ReturnsMappedOrderedPayloadAndPropagatesCancellation()
    {
        var cancellationSource = new CancellationTokenSource();
        var teamId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var handler = new Mock<ILastGameSummaryQueryHandler>();
        handler
            .Setup(candidate => candidate.HandleRecentAsync(3, cancellationSource.Token))
            .ReturnsAsync(
            [
                new LastGameSummaryModel(
                    Guid.NewGuid(),
                    "Fire FC",
                    "Fire FC Thursday",
                    "Caribbean Drive",
                    new DateTime(2026, 7, 24, 2, 30, 0, DateTimeKind.Utc),
                    20,
                    18,
                    2,
                    "Team Demba 2W",
                    1,
                    [
                        new LastGameTeamModel(
                            teamId,
                            "Team Demba",
                            "Demba",
                            "2W",
                            [new LastGameTeamMemberModel(playerId, "Demba", true, 2, 1)]),
                    ],
                    CanLockTeams: true,
                    CanMatchPlayers: true,
                    CanApprovePostGame: true,
                    MatchId: Guid.NewGuid(),
                    CanRateTeammates: true),
            ]);
        var function = CreateFunction(handler.Object);
        var responseBody = new MemoryStream();
        using var services = CreateFunctionServices();
        var context = CreateFunctionContext(services);
        var response = new Mock<HttpResponseData>(context);
        response.SetupProperty(candidate => candidate.StatusCode);
        response.SetupProperty(candidate => candidate.Headers, new HttpHeadersCollection());
        response.SetupProperty(candidate => candidate.Body, responseBody);
        var request = new Mock<HttpRequestData>(context);
        request.Setup(candidate => candidate.CreateResponse()).Returns(response.Object);

        var result = await function.GetRecentGameSummaries(request.Object, cancellationSource.Token);

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        handler.Verify(candidate => candidate.HandleRecentAsync(3, cancellationSource.Token), Times.Once);
        responseBody.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<LastGameSummaryDto[]>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var summary = payload.Should().ContainSingle().Which;
        summary.Title.Should().Be("Fire FC");
        summary.CanLockTeams.Should().BeTrue();
        summary.CanMatchPlayers.Should().BeTrue();
        summary.CanApprovePostGame.Should().BeTrue();
        summary.CanRateTeammates.Should().BeTrue();
        var team = (summary.Teams ?? []).Should().ContainSingle().Which;
        team.TeamId.Should().Be(teamId);
        var member = team.Members.Should().ContainSingle().Which;
        member.PlayerProfileId.Should().Be(playerId);
        member.Goals.Should().Be(2);
        member.Assists.Should().Be(1);
    }

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
    [InlineData(nameof(GameDayFunctions.GetLastGameSummary), AuthenticationPolicies.AuthenticatedPlayer, "get", "game-day/last-game")]
    [InlineData(nameof(GameDayFunctions.GetRecentGames), AuthenticationPolicies.CanManageSessions, "get", "game-day/recent")]
    [InlineData(nameof(GameDayFunctions.GetRecentGameSummaries), AuthenticationPolicies.AuthenticatedPlayer, "get", "game-day/recent-summaries")]
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

    private static GameDayFunctions CreateFunction(ILastGameSummaryQueryHandler handler) =>
        new(
            pickupPalRefreshService: null!,
            contextHandler: null!,
            lastGameSummaryHandler: handler,
            recentGamesHandler: null!,
            getCaptainAssignmentHandler: null!,
            assignCaptainsHandler: null!,
            lockSessionTeamsHandler: null!,
            unlockSessionTeamsHandler: null!,
            getSessionTeamsHandler: null!,
            getTeamDraftHandler: null!,
            saveTeamPicksHandler: null!,
            getPostGameApprovalHandler: null!,
            approvePostGameStatHandler: null!,
            savePostGameTeamResultHandler: null!,
            publishPostGameHandler: null!,
            reopenPostGameResultsHandler: null!,
            linkParticipantHandler: null!,
            getSessionClaimablesHandler: null!,
            getUnlinkedParticipantsHandler: null!,
            getMyClaimableSessionsHandler: null!,
            claimParticipantHandler: null!,
            idempotentRequestExecutor: null!);

    private static ServiceProvider CreateFunctionServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new WorkerOptions
        {
            Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        }));
        return services.BuildServiceProvider();
    }

    private static FunctionContext CreateFunctionContext(IServiceProvider services)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(candidate => candidate.InstanceServices).Returns(services);
        return context.Object;
    }
}
