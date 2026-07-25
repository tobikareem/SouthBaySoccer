using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Groups;
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
        pageModel.IsEmpty.Should().BeFalse();
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

    [Fact]
    public async Task Appearing_LeaderboardRows_ShowRankPositionAndAppearancesCleanly()
    {
        var pageModel = CreatePageModel();

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.Rankings.Count.Should().BeLessThanOrEqualTo(5, "each metric shows only its top 5");
        pageModel.Rankings[0].Detail.Should().NotContainAny("1st", "2nd", "3rd",
            "the rank is already shown as the numbered leading indicator, so it is not repeated as an ordinal");
        pageModel.Rankings[0].Detail.Should().MatchRegex(@"\b\d+ apps?$", "and ends with the appearance count");
        pageModel.Rankings.Should().OnlyContain(row => !row.Detail.Contains("Â"),
            "the middle-dot separator must not be mojibake");
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
                    It.IsAny<Guid?>(),
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
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenPlayerHasNoLinkedGroups_DefaultsToAllAndHidesFilter()
    {
        var groups = new Mock<IGroupsClient>();
        groups.Setup(service => service.GetMyGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupsResponse(IsLinked: true, []));
        var pageModel = CreatePageModel(groupsClient: groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SelectedGroup.Should().Be(LeaderboardGroupOption.All);
        pageModel.HasGroups.Should().BeFalse("only the 'All' option exists, so the filter stays hidden");
    }

    [Fact]
    public async Task Appearing_DefaultsToPrimaryGroup_AndSwitchingGroupReloadsWithGroupId()
    {
        var groupId = Guid.NewGuid();
        var groups = new Mock<IGroupsClient>();
        groups.Setup(service => service.GetMyGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupsResponse(
                IsLinked: true,
                [new GroupChatDto(groupId, "ext@g.us", "Bay Area Soccer", 349, IsLinked: true, IsPrimary: true)]));
        var client = SeedLeaderboardClient();
        var pageModel = CreatePageModel(client: client, groupsClient: groups);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.SelectedGroup!.Id.Should().Be(groupId, "the leaderboard defaults to the player's primary group");

        pageModel.SelectedGroup = LeaderboardGroupOption.All;

        client.Verify(service => service.GetRankingAsync(
            It.IsAny<Guid>(), It.IsAny<LeaderboardMetric>(), groupId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        client.Verify(service => service.GetRankingAsync(
            It.IsAny<Guid>(), It.IsAny<LeaderboardMetric>(), (Guid?)null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
    public async Task Appearing_EmptyRanking_StaysInContentWithInlinePlaceholder()
    {
        var client = new Mock<ILeaderboardClient>();
        client.Setup(service => service.GetRankingAsync(
                It.IsAny<Guid>(),
                It.IsAny<LeaderboardMetric>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardDto(
                SeedFixtures.Season2026Id,
                "Season 2026",
                LeaderboardMetric.Goals,
                string.Empty,
                []));
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        // Content, not a full-screen empty state: the metric tabs must stay visible (wireframe)
        // with the inline "no rankings yet" placeholder shown under them.
        pageModel.State.Should().Be(ViewState.Content);
        pageModel.IsEmpty.Should().BeTrue();
        pageModel.Rankings.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_HttpRequestException_ShowsOfflineState()
    {
        var client = new Mock<ILeaderboardClient>();
        client.Setup(service => service.GetRankingAsync(
                It.IsAny<Guid>(),
                It.IsAny<LeaderboardMetric>(),
                It.IsAny<Guid?>(),
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
                It.IsAny<Guid?>(),
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
            Attribute(element, "Text") == "{Binding Season}");
        // The season chevron badge is now a group filter: a Picker bound to the player's groups.
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Picker" &&
            Attribute(element, "SelectedItem") == "{Binding SelectedGroup, Mode=TwoWay}");
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
        Mock<ILeaderboardNavigator>? navigator = null,
        Mock<IGroupsClient>? groupsClient = null) =>
        new(
            (client ?? SeedLeaderboardClient()).Object,
            (groupsClient ?? GroupsClient()).Object,
            (navigator ?? Navigator()).Object,
            new LeaderboardOptions { SeasonId = SeedFixtures.Season2026Id });

    private static Mock<IGroupsClient> GroupsClient()
    {
        var client = new Mock<IGroupsClient>();
        client.Setup(service => service.GetMyGroupsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MyGroupsResponse(IsLinked: true, []));
        return client;
    }

    private static Mock<ILeaderboardClient> SeedLeaderboardClient()
    {
        var client = new Mock<ILeaderboardClient>();
        foreach (var metric in Enum.GetValues<LeaderboardMetric>())
        {
            client.Setup(service => service.GetRankingAsync(
                    SeedFixtures.Season2026Id,
                    metric,
                    It.IsAny<Guid?>(),
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
