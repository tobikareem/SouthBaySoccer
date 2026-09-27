using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;

namespace SouthBaySoccer.Functions.Tests;

public sealed class AuthenticationMiddlewareTests
{
    [Fact]
    public async Task Invoke_WhenDeletedAccountPresentsValidAdminToken_RejectsBeforeAuthorization()
    {
        using var fixture = new Fixture();
        fixture.Access.Setup(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken))
            .ReturnsAsync(false);
        var nextCalled = false;

        var act = () => new AuthenticationMiddleware().Invoke(fixture.Context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await act.Should().ThrowAsync<UnauthenticatedException>();
        nextCalled.Should().BeFalse();
        fixture.Context.GetCurrentUser().IsAuthenticated.Should().BeFalse();
        fixture.CurrentUser.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_WhenAccountIsActive_PreservesValidatedPrincipal()
    {
        using var fixture = new Fixture();
        fixture.Access.Setup(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken))
            .ReturnsAsync(true);
        var nextCalled = false;

        await new AuthenticationMiddleware().Invoke(fixture.Context, context =>
        {
            nextCalled = true;
            context.GetCurrentUser().UserId.Should().Be(fixture.UserId);
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        fixture.CurrentUser.UserId.Should().Be(fixture.UserId);
        fixture.CurrentUser.IsInRole("Owner").Should().BeTrue();
        fixture.Access.Verify(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken), Times.Once);
    }

    [Fact]
    public async Task Invoke_WhenAccountDeletedBetweenRequests_RechecksSameTokenAndClearsPriorPrincipal()
    {
        using var fixture = new Fixture();
        fixture.Access.SetupSequence(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken))
            .ReturnsAsync(true).ReturnsAsync(false);
        var middleware = new AuthenticationMiddleware();
        await middleware.Invoke(fixture.Context, _ => Task.CompletedTask);
        fixture.CurrentUser.IsAuthenticated.Should().BeTrue();

        var act = () => middleware.Invoke(fixture.Context, _ => Task.CompletedTask);

        await act.Should().ThrowAsync<UnauthenticatedException>();
        fixture.CurrentUser.IsAuthenticated.Should().BeFalse();
        fixture.Access.Verify(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken), Times.Exactly(2));
    }

    [Fact]
    public async Task Invoke_WhenTokenIsInvalid_DoesNotQueryAccount()
    {
        using var fixture = new Fixture();
        fixture.Tokens.Setup(x => x.ValidateAccessToken("token"))
            .Returns(AccessTokenValidationResult.Failed("expired"));

        var act = () => new AuthenticationMiddleware().Invoke(fixture.Context, _ => Task.CompletedTask);

        await act.Should().ThrowAsync<UnauthenticatedException>();
        fixture.Access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invoke_WhenAccountReadFails_DoesNotAuthenticateOrContinue()
    {
        using var fixture = new Fixture();
        fixture.Access.Setup(x => x.IsActiveAsync(fixture.UserId, fixture.Context.CancellationToken))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var nextCalled = false;

        var act = () => new AuthenticationMiddleware().Invoke(fixture.Context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        nextCalled.Should().BeFalse();
        fixture.CurrentUser.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_WhenRequestHasNoBearer_DoesNotQueryAccount()
    {
        using var fixture = new Fixture(bearer: false);
        var nextCalled = false;

        await new AuthenticationMiddleware().Invoke(fixture.Context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        fixture.CurrentUser.IsAuthenticated.Should().BeFalse();
        fixture.Tokens.VerifyNoOtherCalls();
        fixture.Access.VerifyNoOtherCalls();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly CancellationTokenSource cancellation = new();
        public Guid UserId { get; } = Guid.NewGuid();
        public Mock<IAccountAccessValidator> Access { get; } = new();
        public Mock<ITokenService> Tokens { get; } = new();
        public FunctionCurrentUser CurrentUser { get; } = new();
        public FunctionContext Context { get; }

        public Fixture(bool bearer = true)
        {
            Tokens.Setup(x => x.ValidateAccessToken("token")).Returns(AccessTokenValidationResult.Succeeded(
                UserId, ["Owner"], ["ManagePlayers"], DateTime.UtcNow.AddMinutes(15), "key"));
            provider = new ServiceCollection().AddSingleton(Tokens.Object).AddSingleton(Access.Object)
                .AddSingleton<IFunctionCurrentUserAccessor>(CurrentUser).BuildServiceProvider();
            var context = new Mock<FunctionContext>();
            context.Setup(x => x.InstanceServices).Returns(provider);
            context.Setup(x => x.CancellationToken).Returns(cancellation.Token);
            context.Setup(x => x.Items).Returns(new Dictionary<object, object>());
            var binding = new Mock<BindingMetadata>();
            binding.Setup(x => x.Type).Returns("httpTrigger");
            var definition = new Mock<FunctionDefinition>();
            definition.Setup(x => x.InputBindings).Returns(
                new Dictionary<string, BindingMetadata> { ["request"] = binding.Object }.ToImmutableDictionary());
            context.Setup(x => x.FunctionDefinition).Returns(definition.Object);
            var request = new Mock<HttpRequestData>(context.Object);
            var headers = new HttpHeadersCollection();
            if (bearer) headers.Add("Authorization", "Bearer token");
            request.Setup(x => x.Headers).Returns(headers);
            var httpFeature = new Mock<IHttpRequestDataFeature>();
            httpFeature.Setup(x => x.GetHttpRequestDataAsync(context.Object))
                .Returns(new ValueTask<HttpRequestData?>(request.Object));
            var features = new Mock<IInvocationFeatures>();
            features.Setup(x => x.Get<IHttpRequestDataFeature>()).Returns(httpFeature.Object);
            context.Setup(x => x.Features).Returns(features.Object);
            Context = context.Object;
        }

        public void Dispose()
        {
            provider.Dispose();
            cancellation.Dispose();
        }
    }
}
