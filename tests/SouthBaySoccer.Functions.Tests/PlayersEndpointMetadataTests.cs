using System.Reflection;
using System.Text.Json;
using Azure.Core.Serialization;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Players;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.Tests.TestSupport;

namespace SouthBaySoccer.Functions.Tests;

public sealed class PlayersEndpointMetadataTests
{
    [Fact]
    public void GetPlayerDirectory_WhenMetadataResolved_RequiresAuthenticatedPlayerPolicy()
    {
        var method = typeof(PlayersFunctions).GetMethod(nameof(PlayersFunctions.GetPlayerDirectory))
            ?? throw new InvalidOperationException("Missing endpoint GetPlayerDirectory.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(AuthenticationPolicies.AuthenticatedPlayer);
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be("players/directory");
    }

    [Fact]
    public async Task GetPlayerDirectory_WhenProfilesExist_ReturnsExpectedResponseBody()
    {
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var repository = new Mock<IPlayerProfileRepository>();
        repository.Setup(candidate => candidate.ListDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlayerDirectoryReadModel(playerId, "Ada Johnson", "Midfielder", false, 12),
            ]);
        var function = new PlayersFunctions(new GetPlayerDirectoryQueryHandler(repository.Object, new PassThroughReadThroughCache()));
        var responseBody = new MemoryStream();
        using var services = CreateFunctionServices();
        var context = CreateFunctionContext(services);
        var response = new Mock<HttpResponseData>(context);
        response.SetupProperty(candidate => candidate.StatusCode);
        response.SetupProperty(candidate => candidate.Headers, new HttpHeadersCollection());
        response.SetupProperty(candidate => candidate.Body, responseBody);
        var request = new Mock<HttpRequestData>(context);
        request.Setup(candidate => candidate.CreateResponse()).Returns(response.Object);

        var result = await function.GetPlayerDirectory(request.Object, CancellationToken.None);

        result.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        responseBody.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<PlayerDirectoryDto>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        payload!.TotalPlayers.Should().Be(1);
        payload.Players.Should().ContainSingle();
        payload.Players[0].Player.Id.Should().Be(playerId);
        payload.Players[0].Player.DisplayName.Should().Be("Ada Johnson");
        payload.Players[0].Player.Initials.Should().Be("AJ");
        payload.Players[0].Player.Position.Should().Be("Midfielder");
        payload.Players[0].Player.IsGuest.Should().BeFalse();
        payload.Players[0].Subtitle.Should().Be("Midfielder \u00B7 #1");
        payload.Players[0].Matches.Should().Be(12);
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");

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
