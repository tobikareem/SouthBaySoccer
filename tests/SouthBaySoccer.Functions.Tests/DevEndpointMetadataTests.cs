using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Dev;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class DevEndpointMetadataTests
{
    [Fact]
    public void CreateLocalAdminSession_WhenMetadataResolved_AllowsAnonymousLocalBootstrap()
    {
        var method = typeof(DevAuthFunctions).GetMethod(nameof(DevAuthFunctions.CreateLocalAdminSession))
            ?? throw new InvalidOperationException("Missing dev auth endpoint.");

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>().Should().BeNull();
        var trigger = GetHttpTrigger(method);
        trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        trigger.Route.Should().Be("dev/local-admin-session");
    }

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}