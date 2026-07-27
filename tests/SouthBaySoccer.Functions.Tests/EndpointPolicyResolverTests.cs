using FluentAssertions;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class EndpointPolicyResolverTests
{
    private readonly ReflectionEndpointPolicyResolver _resolver = new();

    [Fact]
    public void Resolve_WhenEndpointAllowsAnonymous_ReturnsAnonymousRequirement()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.AnonymousEndpoint));

        var requirement = _resolver.Resolve(entryPoint);

        requirement.Kind.Should().Be(EndpointAccessKind.Anonymous);
        requirement.Policy.Should().BeNull();
    }

    [Fact]
    public void Resolve_WhenEndpointRequiresPolicy_ReturnsPolicyRequirement()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.PolicyEndpoint));

        var requirement = _resolver.Resolve(entryPoint);

        requirement.Kind.Should().Be(EndpointAccessKind.Policy);
        requirement.Policy.Should().Be("CanManageSessions");
    }

    [Fact]
    public void Resolve_WhenEndpointHasNoAccessMetadata_ThrowsEndpointClassificationException()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.UnclassifiedEndpoint));

        var act = () => _resolver.Resolve(entryPoint);

        act.Should().Throw<EndpointClassificationException>()
            .WithMessage("*must declare either AllowAnonymous or RequirePolicy*");
    }

    [Fact]
    public void Resolve_WhenEndpointHasConflictingAccessMetadata_ThrowsEndpointClassificationException()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.ConflictingEndpoint));

        var act = () => _resolver.Resolve(entryPoint);

        act.Should().Throw<EndpointClassificationException>()
            .WithMessage("*both anonymous and policy*");
    }

    [Fact]
    public void Resolve_WhenEndpointHasEmptyPolicy_ThrowsEndpointClassificationException()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.EmptyPolicyEndpoint));

        var act = () => _resolver.Resolve(entryPoint);

        act.Should().Throw<EndpointClassificationException>()
            .WithMessage("*empty authorization policy*");
    }

    [Fact]
    public void Resolve_WhenCalledAgainForSameEntryPoint_ReturnsCachedRequirement()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.PolicyEndpoint));

        var first = _resolver.Resolve(entryPoint);
        var second = _resolver.Resolve(entryPoint);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Resolve_WhenClassificationFails_KeepsThrowingOnRepeatedCalls()
    {
        var entryPoint = EntryPoint(nameof(SampleFunctions.UnclassifiedEndpoint));

        var act = () => _resolver.Resolve(entryPoint);

        act.Should().Throw<EndpointClassificationException>();
        act.Should().Throw<EndpointClassificationException>();
    }

    private static string EntryPoint(string methodName) =>
        $"{typeof(SampleFunctions).FullName}.{methodName}";

    private sealed class SampleFunctions
    {
        [AllowAnonymous]
        public void AnonymousEndpoint()
        {
        }

        [RequirePolicy("CanManageSessions")]
        public void PolicyEndpoint()
        {
        }

        public void UnclassifiedEndpoint()
        {
        }

        [AllowAnonymous]
        [RequirePolicy("CanManageSessions")]
        public void ConflictingEndpoint()
        {
        }

        [RequirePolicy(" ")]
        public void EmptyPolicyEndpoint()
        {
        }
    }
}
