using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Announcements;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Announcements;

public sealed class UnreadAnnouncementSummaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(99)]
    public async Task HandleAsync_WhenAuthenticated_DelegatesCappedSummaryAndPreservesTarget(int count)
    {
        var userId = Guid.NewGuid();
        var profile = new PlayerProfile { IdentityUserId = userId };
        var profiles = new Mock<IPlayerProfileRepository>();
        using var cancellation = new CancellationTokenSource();
        profiles.Setup(x => x.FindByIdentityUserIdAsync(userId, cancellation.Token)).ReturnsAsync(profile);
        var summary = new UnreadAnnouncementSummary(count, count == 0 ? null : Guid.NewGuid());
        var announcements = new Mock<IAnnouncementRepository>(MockBehavior.Strict);
        announcements.Setup(x => x.GetUnreadSummaryForPlayerAsync(profile.Id, 99, cancellation.Token)).ReturnsAsync(summary);
        var handler = new GetUnreadAnnouncementCountQueryHandler(Mock.Of<ICurrentUser>(x => x.UserId == userId),
            profiles.Object, announcements.Object);

        var result = await handler.HandleAsync(new GetUnreadAnnouncementCountQuery(), cancellation.Token);

        result.Should().Be(summary);
        announcements.VerifyAll();
    }

    [Fact]
    public async Task HandleAsync_WhenUnauthenticated_DoesNotQuerySummary()
    {
        var announcements = new Mock<IAnnouncementRepository>(MockBehavior.Strict);
        var handler = new GetUnreadAnnouncementCountQueryHandler(Mock.Of<ICurrentUser>(),
            Mock.Of<IPlayerProfileRepository>(), announcements.Object);

        var act = () => handler.HandleAsync(new GetUnreadAnnouncementCountQuery());

        await act.Should().ThrowAsync<ApplicationUnauthenticatedException>();
        announcements.VerifyNoOtherCalls();
    }
}
