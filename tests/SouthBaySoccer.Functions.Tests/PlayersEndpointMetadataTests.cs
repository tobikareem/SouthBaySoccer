using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Players;
using SouthBaySoccer.Functions.Pipeline;

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

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
