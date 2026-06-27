using FluentAssertions;
using SouthBaySoccer.Functions.Authentication;
using Xunit;

namespace SouthBaySoccer.Functions.Tests;

public sealed class CurrentUserContextTests
{
    [Fact]
    public void FunctionCurrentUser_WhenSetFromValidatedToken_ExposesAuthenticatedPrincipal()
    {
        var userId = Guid.NewGuid();
        var currentUser = new FunctionCurrentUser();

        currentUser.SetCurrentUser(FunctionCurrentUserPrincipal.Authenticated(
            userId,
            ["Player"],
            [AuthenticationPolicies.AuthenticatedPlayer]));

        currentUser.IsAuthenticated.Should().BeTrue();
        currentUser.UserId.Should().Be(userId);
        currentUser.IsInRole("player").Should().BeTrue();
        currentUser.HasPolicy(AuthenticationPolicies.AuthenticatedPlayer).Should().BeTrue();
    }

    [Fact]
    public void FunctionCurrentUser_WhenSetAnonymous_ClearsPriorPrincipal()
    {
        var currentUser = new FunctionCurrentUser();
        currentUser.SetCurrentUser(FunctionCurrentUserPrincipal.Authenticated(
            Guid.NewGuid(),
            ["Player"],
            [AuthenticationPolicies.AuthenticatedPlayer]));

        currentUser.SetAnonymous();

        currentUser.IsAuthenticated.Should().BeFalse();
        currentUser.UserId.Should().BeNull();
        currentUser.IsInRole("Player").Should().BeFalse();
        currentUser.HasPolicy(AuthenticationPolicies.AuthenticatedPlayer).Should().BeFalse();
    }

    [Fact]
    public void FunctionCurrentUserPrincipal_WhenAnonymous_DoesNotTreatClientCallbackAsAuthentication()
    {
        var principal = FunctionCurrentUserPrincipal.Anonymous;

        principal.IsAuthenticated.Should().BeFalse();
        principal.UserId.Should().BeNull();
    }
}
