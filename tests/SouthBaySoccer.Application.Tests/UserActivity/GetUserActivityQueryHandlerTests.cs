using FluentAssertions;
using FluentValidation;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.UserActivity;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.UserActivity;

public sealed class GetUserActivityQueryHandlerTests
{
    [Theory]
    [InlineData("Player")]
    [InlineData("Admin")]
    [InlineData("Captain")]
    public async Task HandleAsync_WhenNotOwner_RejectsWithoutReading(string role)
    {
        var user = User(role);
        var repository = new Mock<IUserActivityRepository>(MockBehavior.Strict);
        var handler = new GetUserActivityQueryHandler(user.Object, repository.Object);

        await ((Func<Task>)(() => handler.HandleAsync(new()))).Should().ThrowAsync<ApplicationForbiddenException>();
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenAnonymous_RejectsWithoutReading()
    {
        var repository = new Mock<IUserActivityRepository>(MockBehavior.Strict);
        var handler = new GetUserActivityQueryHandler(new Mock<ICurrentUser>().Object, repository.Object);

        await ((Func<Task>)(() => handler.HandleAsync(new()))).Should().ThrowAsync<ApplicationUnauthenticatedException>();
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(10001, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task HandleAsync_WhenPaginationInvalid_RejectsBeforeReading(int page, int pageSize)
    {
        var repository = new Mock<IUserActivityRepository>(MockBehavior.Strict);
        var handler = new GetUserActivityQueryHandler(User("Owner").Object, repository.Object);

        await ((Func<Task>)(() => handler.HandleAsync(new(page, pageSize)))).Should().ThrowAsync<ValidationException>();
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10000, 100)]
    public async Task HandleAsync_WhenOwner_ReturnsRequestedPage(int page, int pageSize)
    {
        var result = new UserActivityPageReadModel([], false, null);
        var repository = new Mock<IUserActivityRepository>();
        repository.Setup(x => x.ReadPageAsync(page, pageSize, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var handler = new GetUserActivityQueryHandler(User("Owner").Object, repository.Object);

        (await handler.HandleAsync(new(page, pageSize))).Should().BeSameAs(result);
        repository.Verify(x => x.ReadPageAsync(page, pageSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<ICurrentUser> User(string role)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        user.Setup(x => x.IsInRole(It.IsAny<string>())).Returns((string candidate) => candidate == role);
        return user;
    }
}
