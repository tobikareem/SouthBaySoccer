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
    [InlineData(nameof(AuthenticationFunctions.SignInByPhone), "auth/pickuppal/phone/sign-in", "post")]
    [InlineData(nameof(AuthenticationFunctions.CompleteWhatsAppLogin), "auth/pickuppal/login/complete", "post")]
    [InlineData(nameof(AuthenticationFunctions.ValidateRegistrationToken), "auth/pickuppal/register/validate", "post")]
    [InlineData(nameof(AuthenticationFunctions.CheckEmailAvailability), "auth/pickuppal/register/email-availability", "post")]
    [InlineData(nameof(AuthenticationFunctions.RegisterWithWhatsApp), "auth/pickuppal/register", "post")]
    [InlineData(nameof(AuthenticationFunctions.GetCurrentTermsVersion), "auth/terms/current", "get")]
    [InlineData(nameof(AuthenticationFunctions.Refresh), "auth/refresh", "post")]
    public void AuthEndpoint_WhenAnonymousFlow_DeclaresAllowAnonymous(string methodName, string expectedRoute, string expectedMethod)
    {
        var method = GetEndpoint(methodName);

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<RequirePolicyAttribute>().Should().BeNull();
        var trigger = GetHttpTrigger(method);
        trigger.Route.Should().Be(expectedRoute);
        trigger.Methods.Should().Equal(expectedMethod);
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
        var methods = typeof(AuthenticationFunctions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .ToArray();

        methods.Should().HaveCount(8);
        foreach (var method in methods)
        {
            GetHttpTrigger(method).AuthLevel.Should().Be(AuthorizationLevel.Anonymous);
        }
    }

    [Fact]
    public void AuthEndpoint_WhenLegacyChallengeRoutesRequested_NoLongerExist()
    {
        var routes = typeof(AuthenticationFunctions)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .Select(method => GetHttpTrigger(method).Route)
            .ToArray();

        routes.Should().NotContain(route => route!.StartsWith("auth/whatsapp/challenges", StringComparison.Ordinal));
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
