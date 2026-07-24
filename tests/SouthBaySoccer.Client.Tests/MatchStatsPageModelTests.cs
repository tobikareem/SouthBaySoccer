using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class MatchStatsPageModelTests
{
    [Fact]
    public async Task Appearing_SeedStats_LoadsWireframeCopyTotalsAndTeammates()
    {
        var pageModel = CreatePageModel(client: SeedStatsClient());

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.MatchSubtitle.Should().Be("Sat · Marina Field");
        pageModel.Goals.Should().Be(2);
        pageModel.Assists.Should().Be(1);
        pageModel.SubmitState.Should().Be(MatchStatsSubmitState.Pending);
        pageModel.SubmitButtonText.Should().Be(MatchStatsPageModel.PendingSubmitText);
        pageModel.IsPendingNoteVisible.Should().BeTrue();
        pageModel.TeammateSubmissions.Select(item => (item.Name, item.Detail, item.IsConfirmed))
            .Should()
            .Equal(
                ("Jide D.", "1 goal · 2 assists", true),
                ("Sade M.", "submitted: 1 goal", false),
                ("Tunde B.", "submitted: 2 goals", false));
    }

    [Fact]
    public void CopyConstants_MatchWireframeText_RemainAvailableForBinding()
    {
        MatchStatsPageModel.HeaderTitle.Should().Be("Match stats");
        MatchStatsPageModel.PerformanceSectionTitle.Should().Be("Your performance");
        MatchStatsPageModel.NoticeText.Should().Contain("captain or game admin confirms");
        MatchStatsPageModel.PendingNote.Should().Be("Sent to Pickup Pal · pending captain/admin");
        MatchStatsPageModel.ConfirmSectionTitle.Should().Be("Confirm teammates · captain");
        MatchStatsPageModel.RateLinkText.Should().Be("Rate teammates instead");
    }

    [Fact]
    public void IncrementAndDecrement_EditableTotals_ClampAtZero()
    {
        var pageModel = CreatePageModel();

        pageModel.IncrementGoalsCommand.Execute(null);
        pageModel.IncrementAssistsCommand.Execute(null);
        pageModel.DecrementGoalsCommand.Execute(null);
        pageModel.DecrementGoalsCommand.Execute(null);
        pageModel.DecrementAssistsCommand.Execute(null);
        pageModel.DecrementAssistsCommand.Execute(null);

        pageModel.Goals.Should().Be(0);
        pageModel.Assists.Should().Be(0);
        pageModel.CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_EditableStats_SendsOnceAndLocksPendingState()
    {
        var client = new Mock<IStatsClient>(MockBehavior.Strict);
        client.Setup(service => service.SubmitStatsAsync(
                SeedFixtures.FeaturedMatchId,
                3,
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = CreatePageModel(client: client);
        pageModel.Goals = 3;
        pageModel.Assists = 2;

        await pageModel.SubmitCommand.ExecuteAsync(null);
        await pageModel.SubmitCommand.ExecuteAsync(null);

        pageModel.SubmitState.Should().Be(MatchStatsSubmitState.Pending);
        pageModel.CanSubmit.Should().BeFalse();
        pageModel.CanEdit.Should().BeFalse();
        pageModel.IsPendingNoteVisible.Should().BeTrue();
        client.Verify(service => service.SubmitStatsAsync(
            SeedFixtures.FeaturedMatchId,
            3,
            2,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_WhenServerAllowsConfirming_ExposesTheCaptainConfirmSection()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(It.IsAny<System.Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.MatchStats with { CanConfirmTeammates = true });
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanConfirmTeammates.Should().BeTrue();
    }

    [Fact]
    public async Task Appearing_WhenServerWithholdsConfirming_HidesTheCaptainConfirmSection()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(It.IsAny<System.Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.MatchStats with { CanConfirmTeammates = false });
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanConfirmTeammates.Should().BeFalse("a regular player must not see the captain confirm section");
    }

    [Fact]
    public async Task ConfirmTeammate_UnconfirmedRow_MarksConfirmedOptimistically()
    {
        var playerId = SeedFixtures.MatchStats.TeammateSubmissions[1].Player.Id;
        var client = new Mock<IStatsClient>(MockBehavior.Strict);
        client.Setup(service => service.ConfirmStatsAsync(
                SeedFixtures.FeaturedMatchId,
                playerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        var pageModel = CreatePageModel(client: client);
        var row = TeammateSubmissionItem.From(SeedFixtures.MatchStats.TeammateSubmissions[1]);

        await pageModel.ConfirmTeammateCommand.ExecuteAsync(row);

        row.IsConfirmed.Should().BeTrue();
        client.Verify(service => service.ConfirmStatsAsync(
            SeedFixtures.FeaturedMatchId,
            playerId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_EmptyTeammateSubmissions_KeepsStatsFormVisible()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatsWith(teammates: []));
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.HasTeammateSubmissions.Should().BeFalse();
        pageModel.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public async Task Appearing_HttpRequestException_ShowsOfflineState()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(MatchStatsPageModel.OfflineTitle);
    }

    [Fact]
    public async Task Appearing_UnexpectedException_ShowsErrorState()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(MatchStatsPageModel.ErrorTitle);
    }

    [Fact]
    public async Task NavigationCommands_Invoked_UseMatchStatsNavigator()
    {
        var navigator = new Mock<IMatchStatsNavigator>();
        navigator.Setup(service => service.OpenRateTeammatesAsync(SeedFixtures.FeaturedMatchId, SeedFixtures.CurrentPlayerId, "Sat · Marina Field"))
            .Returns(Task.CompletedTask);
        navigator.Setup(service => service.GoBackAsync()).Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(navigator: navigator);

        await pageModel.OpenRateCommand.ExecuteAsync(null);
        await pageModel.BackCommand.ExecuteAsync(null);

        navigator.Verify(service => service.OpenRateTeammatesAsync(SeedFixtures.FeaturedMatchId, SeedFixtures.CurrentPlayerId, "Sat · Marina Field"), Times.Once);
        navigator.Verify(service => service.GoBackAsync(), Times.Once);
    }

    [Fact]
    public void MatchStatsPageXaml_UsesSharedControlsFontAwesomeAndScrollableLayout()
    {
        var page = LoadXaml("MatchStatsPage.xaml");
        var xaml = page.ToString();

        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "BrandHeader" &&
            Attribute(element, "ShowBack") == "True" &&
            Attribute(element, "BackCommand") != null);
        page.Descendants().Should().Contain(element => element.Name.LocalName == "StateView");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "ScrollView");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "CounterStepper");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "PlayerRow");
        xaml.Should().Contain("FontAwesomeGlyphs.CircleInfo");
        xaml.Should().Contain("FontAwesomeGlyphs.Futbol");
        xaml.Should().Contain("FontAwesomeGlyphs.ShoePrints");
        xaml.Should().Contain("FontAwesomeGlyphs.Plug");
        xaml.Should().Contain("FontAwesomeGlyphs.CircleCheck");
        xaml.Should().Contain("FontAwesomeGlyphs.ChevronRight");
        xaml.Should().Contain("SemanticProperties.Description");
        xaml.Should().NotContain("#");
    }

    private static MatchStatsPageModel CreatePageModel(
        Mock<IStatsClient>? client = null,
        Mock<IMatchStatsNavigator>? navigator = null) =>
        new(
            (client ?? StatsClient()).Object,
            (navigator ?? Navigator()).Object,
            new MatchStatsOptions { MatchId = SeedFixtures.FeaturedMatchId });

    private static Mock<IStatsClient> SeedStatsClient()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(
                SeedFixtures.FeaturedMatchId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.MatchStats);
        return client;
    }

    private static Mock<IStatsClient> StatsClient()
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetMatchStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatsWith(isPending: false));
        client.Setup(service => service.SubmitStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        client.Setup(service => service.ConfirmStatsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        return client;
    }

    private static Mock<IMatchStatsNavigator> Navigator()
    {
        var navigator = new Mock<IMatchStatsNavigator>();
        navigator.Setup(service => service.OpenRateTeammatesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        navigator.Setup(service => service.GoBackAsync()).Returns(Task.CompletedTask);
        return navigator;
    }

    private static MatchStatsDto StatsWith(
        bool isPending = false,
        IReadOnlyList<TeammateStatSubmissionDto>? teammates = null) =>
        SeedFixtures.MatchStats with
        {
            IsPendingConfirmation = isPending,
            TeammateSubmissions = teammates ?? SeedFixtures.MatchStats.TeammateSubmissions
        };

    private static XDocument LoadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return XDocument.Load(path);
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
}

