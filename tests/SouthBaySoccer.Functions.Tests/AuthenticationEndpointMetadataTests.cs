using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class AuthenticationEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(AuthenticationFunctions.RequestWhatsAppChallenge), "auth/whatsapp/challenges")]
    [InlineData(nameof(AuthenticationFunctions.VerifyWhatsAppChallenge), "auth/whatsapp/challenges/verify")]
    [InlineData(nameof(AuthenticationFunctions.Refresh), "auth/refresh")]
    public void AuthEndpoint_WhenAnonymousFlow_DeclaresAllowAnonymous(string methodName, string expectedRoute)
    {
        var method = GetEndpoint(methodName);

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>().Should().BeNull();
        GetHttpTrigger(method).Route.Should().Be(expectedRoute);
    }

    [Fact]
    public void SignOut_WhenEndpointMetadataResolved_RequiresAuthenticatedPlayerPolicy()
    {
        var method = GetEndpoint(nameof(AuthenticationFunctions.SignOut));

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>()!.Policy.Should().Be(AuthenticationPolicies.AuthenticatedPlayer);
        GetHttpTrigger(method).Route.Should().Be("auth/sign-out");
    }

    [Fact]
    public void AuthEndpoint_WhenHttpTriggerConfigured_UsesAnonymousFunctionAuthorization()
    {
        var methods = new[]
        {
            nameof(AuthenticationFunctions.RequestWhatsAppChallenge),
            nameof(AuthenticationFunctions.VerifyWhatsAppChallenge),
            nameof(AuthenticationFunctions.Refresh),
            nameof(AuthenticationFunctions.SignOut),
        };

        foreach (var methodName in methods)
        {
            GetHttpTrigger(GetEndpoint(methodName)).AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        }
    }

    private static MethodInfo GetEndpoint(string methodName) =>
        typeof(AuthenticationFunctions).GetMethod(methodName)
        ?? throw new InvalidOperationException($"Missing endpoint {methodName}.");

    private static HttpTriggerAttribute GetHttpTrigger(MethodInfo method) =>
        method.GetParameters()
            .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
            .FirstOrDefault(attribute => attribute is not null)
        ?? throw new InvalidOperationException($"Missing HTTP trigger metadata on {method.Name}.");
}
