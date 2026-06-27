using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class EndpointAuthorizerTests
{
    [Fact]
    public void Authorize_WhenEndpointAllowsAnonymous_DoesNotRequireCurrentUser()
    {
        var resolver = new Mock<IEndpointPolicyResolver>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUser>(MockBehavior.Strict);
        resolver
            .Setup(x => x.Resolve("Functions.Auth"))
            .Returns(EndpointAccessRequirement.Anonymous);
        var authorizer = new EndpointAuthorizer(resolver.Object, currentUser.Object);

        authorizer.Authorize("Functions.Auth");

        resolver.Verify(x => x.Resolve("Functions.Auth"), Times.Once);
        currentUser.VerifyNoOtherCalls();
    }

    [Fact]
    public void Authorize_WhenPolicyEndpointHasAnonymousUser_ThrowsUnauthenticatedException()
    {
        var resolver = new Mock<IEndpointPolicyResolver>();
        var currentUser = new Mock<ICurrentUser>();
        resolver
            .Setup(x => x.Resolve("Functions.Admin"))
            .Returns(EndpointAccessRequirement.RequirePolicy("CanManageSessions"));
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var authorizer = new EndpointAuthorizer(resolver.Object, currentUser.Object);

        var act = () => authorizer.Authorize("Functions.Admin");

        act.Should().Throw<UnauthenticatedException>();
    }

    [Fact]
    public void Authorize_WhenPolicyEndpointUserLacksPolicy_ThrowsForbiddenException()
    {
        var resolver = new Mock<IEndpointPolicyResolver>();
        var currentUser = new Mock<ICurrentUser>();
        resolver
            .Setup(x => x.Resolve("Functions.Admin"))
            .Returns(EndpointAccessRequirement.RequirePolicy("CanManageSessions"));
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(false);
        var authorizer = new EndpointAuthorizer(resolver.Object, currentUser.Object);

        var act = () => authorizer.Authorize("Functions.Admin");

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Authorize_WhenPolicyEndpointUserHasPolicy_DoesNotThrow()
    {
        var resolver = new Mock<IEndpointPolicyResolver>();
        var currentUser = new Mock<ICurrentUser>();
        resolver
            .Setup(x => x.Resolve("Functions.Admin"))
            .Returns(EndpointAccessRequirement.RequirePolicy("CanManageSessions"));
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.Setup(x => x.HasPolicy("CanManageSessions")).Returns(true);
        var authorizer = new EndpointAuthorizer(resolver.Object, currentUser.Object);

        var act = () => authorizer.Authorize("Functions.Admin");

        act.Should().NotThrow();
    }
}


