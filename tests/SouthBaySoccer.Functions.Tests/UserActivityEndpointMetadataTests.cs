using System.Text.Json;
using Azure.Core.Serialization;
using FluentValidation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Features.UserActivity;
using SouthBaySoccer.Contracts.UserActivity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;
using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker.Http;
using SouthBaySoccer.Functions.Authentication;
using SouthBaySoccer.Functions.Pipeline;
using SouthBaySoccer.Functions.UserActivity;

namespace SouthBaySoccer.Functions.Tests;

public sealed class UserActivityEndpointMetadataTests
{
    [Fact]
    public void GetUserActivity_WhenMetadataResolved_RequiresOwnerPolicy()
    {
        var method = typeof(UserActivityFunctions).GetMethod(nameof(UserActivityFunctions.GetUserActivity))
            ?? throw new InvalidOperationException("Missing user activity endpoint.");

        method.GetCustomAttribute<RequirePolicyAttribute>()?.Policy.Should().Be(AuthenticationPolicies.IsSuperAdmin);
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        var trigger = method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).Single(attribute => attribute is not null);
        trigger?.Route.Should().Be("admin/user-activity");
        trigger?.Methods.Should().Equal("get");
    }

    [Fact]
    public async Task GetUserActivity_WhenOwnerRequestsPage_MapsSummaryAndUtcWithoutCaching()
    {
        var at = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        var repository = new Mock<IUserActivityRepository>();
        var playerId = Guid.NewGuid();
        repository.Setup(x => x.ReadPageAsync(2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserActivityPageReadModel([
                new(Guid.NewGuid(), playerId, "Ada", UserActivityType.SignUp, at, at, at, 0,
                    [new UserActivityGroupReadModel("Soccer", "Pending")]),
            ], true, at));
        var function = new UserActivityFunctions(new GetUserActivityQueryHandler(Owner(), repository.Object));
        using var services = Services();
        var (request, response) = Request(services, "?page=2&pageSize=10");

        await function.GetUserActivity(request, CancellationToken.None);

        response.Headers.GetValues("Cache-Control").Should().Contain("no-store");
        response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<UserActivityPageDto>(response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotBeNull();
        var page = payload ?? throw new InvalidOperationException("Missing payload.");
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(10);
        page.HasMore.Should().BeTrue();
        page.TrackingStartedAtUtc?.Kind.Should().Be(DateTimeKind.Utc);
        var row = page.Items.Should().ContainSingle().Subject;
        row.PlayerProfileId.Should().Be(playerId);
        row.ActivityType.Should().Be("SignUp");
        row.SignInCount.Should().Be(0);
        row.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        row.FirstRecordedActivityAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        row.LastRecordedActivityAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        row.Groups.Should().ContainSingle().Which.Status.Should().Be("Pending");
    }

    [Theory]
    [InlineData("?page=invalid")]
    [InlineData("?pageSize=2147483648")]
    [InlineData("?page=1&page=2")]
    public async Task GetUserActivity_WhenQueryMalformed_RejectsBeforeRepository(string query)
    {
        var repository = new Mock<IUserActivityRepository>(MockBehavior.Strict);
        var function = new UserActivityFunctions(new GetUserActivityQueryHandler(Owner(), repository.Object));
        using var services = Services();
        var (request, _) = Request(services, query);

        Func<Task> action = () => function.GetUserActivity(request, CancellationToken.None);
        await action.Should().ThrowAsync<ValidationException>();
        repository.VerifyNoOtherCalls();
    }

    private static ICurrentUser Owner()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        user.Setup(x => x.IsInRole("Owner")).Returns(true);
        return user.Object;
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new WorkerOptions
        {
            Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        }));
        return services.BuildServiceProvider();
    }

    private static (HttpRequestData Request, HttpResponseData Response) Request(IServiceProvider services, string query)
    {
        var context = new Mock<FunctionContext>();
        context.SetupGet(x => x.InstanceServices).Returns(services);
        var response = new Mock<HttpResponseData>(context.Object);
        response.SetupProperty(x => x.StatusCode);
        response.SetupProperty(x => x.Headers, new HttpHeadersCollection());
        response.SetupProperty(x => x.Body, new MemoryStream());
        var request = new Mock<HttpRequestData>(context.Object);
        request.Setup(x => x.Url).Returns(new Uri("https://localhost/api/admin/user-activity" + query));
        request.Setup(x => x.CreateResponse()).Returns(response.Object);
        return (request.Object, response.Object);
    }
}
