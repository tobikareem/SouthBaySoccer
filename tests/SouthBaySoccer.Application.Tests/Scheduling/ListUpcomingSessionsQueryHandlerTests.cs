using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Abstractions.Authentication;
using SouthBaySoccer.Application.Abstractions.Time;
using SouthBaySoccer.Application.Features.Scheduling;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Entities.Scheduling;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Scheduling;

public sealed class ListUpcomingSessionsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenFeedContainsFullSession_ProjectsActualCountsAndJoinCapability()
    {
        var now = Utc(2026, 7, 23, 12, 0);
        var profile = Profile();
        var session = SessionAt(now.AddDays(2), capacity: 20);
        var repository = new Mock<ISessionRepository>();
        repository
            .Setup(x => x.ListUpcomingFeedAsync(
                now,
                25,
                profile.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SessionFeedRecord(session, "Stanford Turf", 20, 3, false, false)
            ]);
        var handler = CreateHandler(now, profile, repository.Object);

        var result = await handler.HandleAsync();

        result.Should().ContainSingle();
        result[0].VenueName.Should().Be("Stanford Turf");
        result[0].GoingCount.Should().Be(20);
        result[0].WaitlistCount.Should().Be(3);
        result[0].IsFull.Should().BeTrue();
        result[0].CanJoinWaitlist.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenCallerIsAlreadyWaitlisted_DoesNotOfferDuplicateJoin()
    {
        var now = Utc(2026, 7, 23, 12, 0);
        var profile = Profile();
        var session = SessionAt(now.AddDays(2), capacity: 20);
        var repository = new Mock<ISessionRepository>();
        repository
            .Setup(x => x.ListUpcomingFeedAsync(
                now,
                25,
                profile.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SessionFeedRecord(session, "Stanford Turf", 20, 4, false, true)
            ]);
        var handler = CreateHandler(now, profile, repository.Object);

        var result = await handler.HandleAsync();

        result[0].IsCurrentPlayerWaitlisted.Should().BeTrue();
        result[0].CanJoinWaitlist.Should().BeFalse();
    }

    private static ListUpcomingSessionsQueryHandler CreateHandler(
        DateTime now,
        PlayerProfile profile,
        ISessionRepository repository)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(profile.IdentityUserId);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        var profiles = new Mock<IPlayerProfileRepository>();
        profiles
            .Setup(x => x.FindByIdentityUserIdAsync(
                profile.IdentityUserId!.Value,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return new ListUpcomingSessionsQueryHandler(
            currentUser.Object,
            clock.Object,
            profiles.Object,
            repository);
    }

    private static PlayerProfile Profile() => new()
    {
        Id = Guid.NewGuid(),
        IdentityUserId = Guid.NewGuid(),
        DisplayName = "Ada Green",
    };

    private static Session SessionAt(DateTime startsAtUtc, int capacity) => new()
    {
        Id = Guid.NewGuid(),
        SeasonId = Guid.NewGuid(),
        VenueId = Guid.NewGuid(),
        Title = "Stanford Turf - 5v5",
        Format = "5v5",
        Capacity = capacity,
        TeamCount = 2,
        StartsAtUtc = startsAtUtc,
        CheckInOpensAtUtc = startsAtUtc.AddMinutes(-10),
        CheckInClosesAtUtc = startsAtUtc.AddMinutes(5),
        RsvpDeadlineUtc = startsAtUtc.AddHours(-1),
        Status = SessionStatus.Published,
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
