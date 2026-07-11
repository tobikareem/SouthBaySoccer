using FluentAssertions;
using Moq;
using SouthBaySoccer.Application.Features.Players;
using SouthBaySoccer.Domain.Interfaces.Repositories;

namespace SouthBaySoccer.Application.Tests.Players;

public sealed class GetPlayerDirectoryQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenProfilesExist_ReturnsDirectoryRowsInRepositoryOrder()
    {
        var repository = new Mock<IPlayerProfileRepository>();
        repository
            .Setup(x => x.ListDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlayerDirectoryReadModel(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Ada Johnson",
                    "Midfielder",
                    false,
                    12),
                new PlayerDirectoryReadModel(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Guest Player",
                    "Forward",
                    true,
                    1),
            ]);
        var handler = new GetPlayerDirectoryQueryHandler(repository.Object);

        var result = await handler.HandleAsync();

        result.Title.Should().Be("Players");
        result.Subtitle.Should().Be("Search the crew and open career stats.");
        result.TotalPlayers.Should().Be(2);
        result.Players[0].Player.DisplayName.Should().Be("Ada Johnson");
        result.Players[0].Player.Initials.Should().Be("AJ");
        result.Players[0].Subtitle.Should().Be("Midfielder \u00B7 #1");
        result.Players[0].Matches.Should().Be(12);
        result.Players[1].Player.IsGuest.Should().BeTrue();
        result.Players[1].Subtitle.Should().Be("Guest \u00B7 #2");
    }

    [Fact]
    public async Task HandleAsync_WhenDisplayNameIsBlank_UsesFallbackInitialsMatchingClient()
    {
        var repository = new Mock<IPlayerProfileRepository>();
        repository
            .Setup(x => x.ListDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlayerDirectoryReadModel(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    " ",
                    "Keeper",
                    false,
                    0),
            ]);
        var handler = new GetPlayerDirectoryQueryHandler(repository.Object);

        var result = await handler.HandleAsync();

        result.Players.Should().ContainSingle();
        // "SB" matches ApiProfileClient.BuildInitials' fallback so the same player renders
        // identically whether the initials came from the server or the MAUI client's local fallback.
        result.Players[0].Player.Initials.Should().Be("SB");
    }
}
