using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Common;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Domain.Entities.Identity;
using SouthBaySoccer.Domain.Enumerations;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Players;

public sealed class GetPlayerProfileQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenProfileExists_ReturnsProfileStatsAndRecentForm()
    {
        var playerProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var profileRepository = new Mock<IPlayerProfileRepository>();
        profileRepository
            .Setup(x => x.FindProfileAsync(playerProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerProfile
            {
                Id = playerProfileId,
                DisplayName = "Ada Johnson",
                PreferredPosition = "Midfielder",
                Role = PlayerRole.Captain,
            });
        var statsRepository = new Mock<IStatsRepository>();
        statsRepository
            .Setup(x => x.GetPlayerStatsAsync(playerProfileId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerStatSummaryReadModel(
                playerProfileId,
                "Ada Johnson",
                "Midfielder",
                false,
                null,
                12,
                4,
                5,
                4.6m,
                8,
                7,
                2));
        statsRepository
            .Setup(x => x.ListPlayerRecentFormAsync(playerProfileId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlayerRecentFormReadModel(Guid.NewGuid(), Utc(2026, 7, 1), 2, 1, 0, 0),
                new PlayerRecentFormReadModel(Guid.NewGuid(), Utc(2026, 6, 24), 2, 0, 1, 0),
            ]);
        var handler = new GetPlayerProfileQueryHandler(profileRepository.Object, statsRepository.Object);

        var result = await handler.HandleAsync(playerProfileId);

        result.PlayerProfileId.Should().Be(playerProfileId);
        result.DisplayName.Should().Be("Ada Johnson");
        result.Initials.Should().Be("AJ");
        result.Role.Should().Be(PlayerRole.Captain.ToString());
        result.CareerStats.Matches.Should().Be(12);
        result.CareerStats.Goals.Should().Be(4);
        result.CareerStats.Assists.Should().Be(5);
        result.CareerStats.AverageRating.Should().Be(4.6m);
        result.CareerStats.MvpAwards.Should().Be(2);
        result.CareerStats.Likes.Should().Be(7);
        result.RecentForm.Should().Equal(
            PlayerProfileRecentFormOutcome.Win,
            PlayerProfileRecentFormOutcome.Draw);
    }

    [Fact]
    public async Task HandleAsync_WhenProfileIsMissing_ThrowsNotFound()
    {
        var playerProfileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var profileRepository = new Mock<IPlayerProfileRepository>();
        profileRepository
            .Setup(x => x.FindProfileAsync(playerProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerProfile?)null);
        var handler = new GetPlayerProfileQueryHandler(
            profileRepository.Object,
            Mock.Of<IStatsRepository>());

        var act = async () => await handler.HandleAsync(playerProfileId);

        await act.Should().ThrowAsync<ApplicationNotFoundException>()
            .WithMessage("Player profile was not found.");
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
