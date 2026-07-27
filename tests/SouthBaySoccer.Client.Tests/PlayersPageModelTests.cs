using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;
using SouthBaySoccer.Services.Clients.Caching;

namespace SouthBaySoccer.Client.Tests;

public class PlayersPageModelTests
{
    [Fact]
    public async Task Appearing_SeedDirectory_LoadsWireframeHeaderCountAndPlayers()
    {
        var pageModel = CreatePageModel();

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Title.Should().Be("Players");
        pageModel.Subtitle.Should().Be("Search the crew and open career stats.");
        pageModel.TotalPlayers.Should().Be(24);
        pageModel.Players.Should().HaveCount(24);
        pageModel.Players[0].Name.Should().Be("Tobi Kareem");
        pageModel.Players[0].Detail.Should().Contain("24 matches");
        pageModel.Players[4].IsGuest.Should().BeTrue();
        pageModel.Players[4].TrailingText.Should().Be("guest");
        pageModel.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData("kola", "Kola T.")]
    [InlineData("goalkeeper", "Sade M.", "Femi A.", "Ngozi F.")]
    [InlineData("guest", "Tunde B.")]
    public async Task SearchQuery_NamePositionOrRole_FiltersPlayers(
        string query,
        params string[] expectedNames)
    {
        var pageModel = CreatePageModel();
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SearchQuery = query;

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Players.Select(player => player.Name).Should().Equal(expectedNames);
    }

    [Fact]
    public async Task SearchQuery_NoMatches_ShowsEmptyStateWithoutDroppingLoadedCount()
    {
        var pageModel = CreatePageModel();
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.SearchQuery = "not on roster";

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(PlayersPageModel.NoMatchesTitle);
        pageModel.TotalPlayers.Should().Be(24);
        pageModel.Players.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenPlayer_RowSelected_NavigatesToExistingProfileRoute()
    {
        var navigator = new Mock<IPlayersNavigator>();
        navigator.Setup(service => service.OpenPlayerProfileAsync(SeedFixtures.Players[1].Id))
            .Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(navigator: navigator);

        await pageModel.OpenPlayerCommand.ExecuteAsync(SeedFixtures.Players[1].Id);

        navigator.Verify(service => service.OpenPlayerProfileAsync(SeedFixtures.Players[1].Id), Times.Once);
    }

    [Fact]
    public async Task Appearing_EmptyDirectory_ShowsEmptyState()
    {
        var client = new Mock<IPlayersClient>();
        client.Setup(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerDirectoryDto("Players", "Search the crew and open career stats.", 0, []));
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(PlayersPageModel.EmptyTitle);
        pageModel.Players.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_HttpRequestException_ShowsOfflineState()
    {
        var client = new Mock<IPlayersClient>();
        client.Setup(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(PlayersPageModel.OfflineTitle);
    }

    [Fact]
    public async Task Appearing_UnexpectedException_ShowsErrorState()
    {
        var client = new Mock<IPlayersClient>();
        client.Setup(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(PlayersPageModel.ErrorTitle);
    }

    [Fact]
    public async Task Refresh_AfterOffline_ReRequestsAndRecoversToContent()
    {
        var directory = await new SeedPlayersClient().GetDirectoryAsync(CancellationToken.None);
        var client = new Mock<IPlayersClient>();
        client.SetupSequence(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException())
            .ReturnsAsync(directory);
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RefreshCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Players.Should().HaveCount(directory.Players.Count);
        pageModel.IsBusy.Should().BeFalse();
        client.Verify(
            service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Refresh_WhenCanceled_PropagatesCancellationAndClearsBusyState()
    {
        var requestStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Mock<IPlayersClient>();
        client.Setup(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async cancellationToken =>
            {
                requestStarted.SetResult(cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The canceled request should not complete.");
            });
        var pageModel = CreatePageModel(client: client);

        var refresh = pageModel.RefreshCommand.ExecuteAsync(null);
        var observedToken = await requestStarted.Task;
        pageModel.RefreshCommand.Cancel();

        var act = async () => await refresh;

        await act.Should().ThrowAsync<OperationCanceledException>();
        observedToken.IsCancellationRequested.Should().BeTrue();
        pageModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void PlayersPageXaml_UsesDirectoryControlsSearchAndFontAwesome()
    {
        var page = LoadXaml("PlayersPage.xaml");
        var xml = page.ToString();

        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "StateView" &&
            Attribute(element, "Glyph")!.Contains("FontAwesomeGlyphs.Users"));
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Entry" &&
            Attribute(element, "Text") == "{Binding SearchQuery}" &&
            Attribute(element, "SemanticProperties.Description") == "Search players");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "PlayerRow");
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Label" &&
            Attribute(element, "Text")!.Contains("FontAwesomeGlyphs.ChevronRight") &&
            Attribute(element, "FontFamily") == "FontAwesomeSolid");
        page.Descendants().Should().NotContain(element => element.Name.LocalName == "CollectionView");
        xml.Should().NotContain("#");
    }

    private static PlayersPageModel CreatePageModel(
        Mock<IPlayersClient>? client = null,
        Mock<IPlayersNavigator>? navigator = null) =>
        new(
            (client ?? SeedPlayersClient()).Object,
            (navigator ?? Navigator()).Object,
            new ClientResponseCache(TimeProvider.System));

    private static Mock<IPlayersClient> SeedPlayersClient()
    {
        var client = new Mock<IPlayersClient>();
        client.Setup(service => service.GetDirectoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SouthBaySoccer.SeedData.SeedPlayersClient().GetDirectoryAsync(CancellationToken.None).GetAwaiter().GetResult());
        return client;
    }

    private static Mock<IPlayersNavigator> Navigator()
    {
        var navigator = new Mock<IPlayersNavigator>();
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
