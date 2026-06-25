using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Leaderboards;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class LeaderboardPageModelTests
{
    [Fact]
    public async Task Appearing_SeedGoalsRanking_LoadsSeasonMetricsRowsAndNote()
    {
        var pageModel = CreatePageModel();

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Season.Should().Be("Season 2026");
        pageModel.Metrics.Select(metric => metric.Label)
            .Should().Equal("Goals", "Assists", "Rating", "MVP");
        pageModel.SelectedMetric.Should().Be(LeaderboardMetric.Goals);
        pageModel.Rankings.Select(row => row.Rank).Should().Equal(1, 2, 3, 4, 5);
        pageModel.Rankings[0].IsLeader.Should().BeTrue();
        pageModel.Rankings[0].RankIndicator.Should().Be(Fonts.FontAwesomeGlyphs.Trophy);
        pageModel.Rankings[0].RankFontFamily.Should().Be("FontAwesomeSolid");
        pageModel.Rankings[1].RankFontFamily.Should().Be("InterSemibold");
        pageModel.Note.Should().Contain("ties use fewer appearances");
    }

    [Theory]
    [InlineData(0, LeaderboardMetric.Goals, "12")]
    [InlineData(1, LeaderboardMetric.Assists, "11")]
    [InlineData(2, LeaderboardMetric.Rating, "8.4")]
    [InlineData(3, LeaderboardMetric.Mvp, "4")]
    public async Task SelectMetric_IndexChanged_RequeriesAndFormatsMetric(
        int selectedIndex,
        LeaderboardMetric expectedMetric,
        string expectedLeaderValue)
    {
        var client = new Mock<ILeaderboardClient>();
        foreach (var metric in Enum.GetValues<LeaderboardMetric>())
        {
            client.Setup(service => service.GetRankingAsync(
                    SeedFixtures.Season2026Id,
                    metric,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(SeedFixtures.Leaderboards[metric]);
        }
        var pageModel = CreatePageModel(client: client);

        pageModel.SelectedMetricIndex = selectedIndex;
        await pageModel.SelectMetricCommand.ExecuteAsync(pageModel.Metrics[selectedIndex]);

        pageModel.SelectedMetric.Should().Be(expectedMetric);
        pageModel.Rankings[0].Value.Should().Be(expectedLeaderValue);
        client.Verify(service => service.GetRankingAsync(
            SeedFixtures.Season2026Id,
            expectedMetric,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenPlayer_RowSelected_NavigatesOnceToPlayerProfile()
    {
        var navigator = new Mock<ILeaderboardNavigator>();
        navigator.Setup(service => service.OpenPlayerProfileAsync(SeedFixtures.CurrentPlayerId))
            .Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(navigator: navigator);

        await pageModel.OpenPlayerCommand.ExecuteAsync(SeedFixtures.CurrentPlayerId);

        navigator.Verify(service => service.OpenPlayerProfileAsync(SeedFixtures.CurrentPlayerId), Times.Once);
    }

    [Fact]
    public async Task Appearing_EmptyRanking_ShowsEmptyState()
    {
        var client = new Mock<ILeaderboardClient>();
        client.Setup(service => service.GetRankingAsync(
                It.IsAny<Guid>(),
                It.IsAny<LeaderboardMetric>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardDto(
                SeedFixtures.Season2026Id,
                "Season 2026",
                LeaderboardMetric.Goals,
                string.Empty,
                []));
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(LeaderboardPageModel.EmptyTitle);
        pageModel.Rankings.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_HttpRequestException_ShowsOfflineState()
    {
        var client = new Mock<ILeaderboardClient>();
        client.Setup(service => service.GetRankingAsync(
                It.IsAny<Guid>(),
                It.IsAny<LeaderboardMetric>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(LeaderboardPageModel.OfflineTitle);
    }

    [Fact]
    public async Task Appearing_UnexpectedException_ShowsErrorState()
    {
        var client = new Mock<ILeaderboardClient>();
        client.Setup(service => service.GetRankingAsync(
                It.IsAny<Guid>(),
                It.IsAny<LeaderboardMetric>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(LeaderboardPageModel.ErrorTitle);
    }

    [Fact]
    public void StatsPageXaml_ImplementedLeaderboard_UsesSeedBackedControlsAndFontAwesome()
    {
        var page = LoadXaml("StatsPage.xaml");

        page.Descendants().Should().NotContain(element => element.Name.LocalName == "BrandHeader");
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Label" &&
            Attribute(element, "Text") == "Leaderboard" &&
            Attribute(element, "Style") == "{StaticResource TextH1}");
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Badge" &&
            Attribute(element, "Text") == "{Binding Season}" &&
            Attribute(element, "Glyph") != null &&
            Attribute(element, "Glyph")!.Contains("FontAwesomeGlyphs.ChevronRight"));
        page.Descendants().Should().Contain(element => element.Name.LocalName == "SegmentedControl");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "PlayerRow");
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Label" &&
            Attribute(element, "Text") != null && Attribute(element, "Text")!.Contains("FontAwesomeGlyphs.Trophy") &&
            Attribute(element, "FontFamily") == "FontAwesomeSolid");
        page.Descendants().Should().NotContain(element => element.Name.LocalName == "CollectionView");
        page.Descendants().Should().NotContain(element => element.Name.LocalName == "AdaptiveTrigger");
        page.ToString().Should().NotContain("Stats are coming soon");
        page.ToString().Should().NotContain("#");
    }

    private static LeaderboardPageModel CreatePageModel(
        Mock<ILeaderboardClient>? client = null,
        Mock<ILeaderboardNavigator>? navigator = null) =>
        new(
            (client ?? SeedLeaderboardClient()).Object,
            (navigator ?? Navigator()).Object,
            new LeaderboardOptions { SeasonId = SeedFixtures.Season2026Id });

    private static Mock<ILeaderboardClient> SeedLeaderboardClient()
    {
        var client = new Mock<ILeaderboardClient>();
        foreach (var metric in Enum.GetValues<LeaderboardMetric>())
        {
            client.Setup(service => service.GetRankingAsync(
                    SeedFixtures.Season2026Id,
                    metric,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(SeedFixtures.Leaderboards[metric]);
        }

        return client;
    }

    private static Mock<ILeaderboardNavigator> Navigator()
    {
        var navigator = new Mock<ILeaderboardNavigator>();
        navigator.Setup(service => service.OpenPlayerProfileAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        return navigator;
    }

    private static XDocument LoadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return XDocument.Load(path);
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
}
