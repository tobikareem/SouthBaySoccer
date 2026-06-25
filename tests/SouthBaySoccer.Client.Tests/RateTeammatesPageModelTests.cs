using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Common;
using SouthBaySoccer.Contracts.Players;
using SouthBaySoccer.Contracts.Stats;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class RateTeammatesPageModelTests
{
    [Fact]
    public async Task Appearing_SeedTeammates_LoadsWireframeRowsAndExcludesRater()
    {
        var pageModel = CreatePageModel();

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.MatchSubtitle.Should().Be("Sat · Marina Field");
        pageModel.Teammates.Select(row => (row.Name, row.Detail, row.Initials, row.Rating))
            .Should().Equal(
                ("Kola T.", "2 goals", "KT", 9),
                ("Jide D.", "1 assist", "JD", 7),
                ("Sade M.", "clean sheet", "SM", 8));
        pageModel.Teammates.Should().NotContain(row => row.PlayerId == SeedFixtures.CurrentPlayerId);
        pageModel.SelectedMvp.Should().BeSameAs(pageModel.Teammates[2]);
    }

    [Theory]
    [InlineData(-2.4, 0)]
    [InlineData(6.6, 7)]
    [InlineData(12, 10)]
    public async Task RatingValue_WhenChanged_CoercesToIntegerZeroThroughTen(double value, int expected)
    {
        var pageModel = CreatePageModel();
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var row = pageModel.Teammates[0];

        row.RatingValue = value;

        row.Rating.Should().Be(expected);
        row.RatingText.Should().Be(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ToggleLike_TargetRow_FlipsOnlyThatTeammate()
    {
        var pageModel = CreatePageModel();
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var first = pageModel.Teammates[0];
        var second = pageModel.Teammates[1];

        pageModel.ToggleLikeCommand.Execute(first);

        first.Liked.Should().BeTrue();
        second.Liked.Should().BeTrue("the fixture starts liked and should not be changed");

        pageModel.ToggleLikeCommand.Execute(first);

        first.Liked.Should().BeFalse();
        second.Liked.Should().BeTrue();
    }

    [Fact]
    public async Task SelectMvp_ChangingSelection_KeepsSingleMvpAndClearsOnReselect()
    {
        var pageModel = CreatePageModel();
        await pageModel.AppearingCommand.ExecuteAsync(null);
        var first = pageModel.Teammates[0];
        var third = pageModel.Teammates[2];

        pageModel.SelectMvpCommand.Execute(first);

        first.IsMvp.Should().BeTrue();
        third.IsMvp.Should().BeFalse();
        pageModel.Teammates.Count(row => row.IsMvp).Should().Be(1);

        pageModel.SelectMvpCommand.Execute(first);

        pageModel.SelectedMvp.Should().BeNull();
        pageModel.Teammates.Should().OnlyContain(row => !row.IsMvp);
    }

    [Fact]
    public async Task SubmitRatings_ContentRows_SendsRatingsLikesAndSingleMvpExcludingRater()
    {
        var client = SeedStatsClient();
        IReadOnlyList<TeammateRatingDto>? submitted = null;
        client.Setup(service => service.SubmitRatingsAsync(
                SeedFixtures.FeaturedMatchId,
                SeedFixtures.CurrentPlayerId,
                It.IsAny<IReadOnlyList<TeammateRatingDto>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, IReadOnlyList<TeammateRatingDto>, CancellationToken>(
                (_, _, ratings, _) => submitted = ratings)
            .ReturnsAsync(ClientCommandResult.Success);
        var navigator = Navigator();
        var pageModel = CreatePageModel(client: client, navigator: navigator);
        await pageModel.AppearingCommand.ExecuteAsync(null);
        pageModel.Teammates[0].Rating = 6;
        pageModel.ToggleLikeCommand.Execute(pageModel.Teammates[0]);
        pageModel.SelectMvpCommand.Execute(pageModel.Teammates[1]);

        await pageModel.SubmitRatingsCommand.ExecuteAsync(null);

        submitted.Should().NotBeNull();
        submitted!.Should().HaveCount(3);
        submitted.Should().NotContain(rating => rating.PlayerId == SeedFixtures.CurrentPlayerId);
        submitted.Single(rating => rating.PlayerId == pageModel.Teammates[0].PlayerId).Rating.Should().Be(6);
        submitted.Single(rating => rating.PlayerId == pageModel.Teammates[0].PlayerId).IsLiked.Should().BeTrue();
        submitted.Single(rating => rating.IsMvp).PlayerId.Should().Be(pageModel.Teammates[1].PlayerId);
        navigator.Verify(service => service.GoBackAsync(), Times.Once);
    }

    [Fact]
    public async Task SubmitRatings_WhileBusy_DoesNotSubmitTwice()
    {
        var completion = new TaskCompletionSource<ClientCommandResult>();
        var client = SeedStatsClient();
        client.Setup(service => service.SubmitRatingsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<TeammateRatingDto>>(),
                It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var pageModel = CreatePageModel(client: client);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        var firstSubmit = pageModel.SubmitRatingsCommand.ExecuteAsync(null);
        await Task.Yield();
        var secondSubmit = pageModel.SubmitRatingsCommand.ExecuteAsync(null);
        completion.SetResult(ClientCommandResult.Success);
        await Task.WhenAll(firstSubmit, secondSubmit);

        client.Verify(service => service.SubmitRatingsAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<TeammateRatingDto>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Appearing_EmptyResult_ShowsEmptyState()
    {
        var client = SeedStatsClient([]);
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.StateTitle.Should().Be(RateTeammatesPageModel.EmptyTitle);
        pageModel.Teammates.Should().BeEmpty();
    }

    [Fact]
    public async Task Appearing_FailureStates_MapToStateViewAndRetryReloads()
    {
        var calls = 0;
        var client = SeedStatsClient();
        client.Setup(service => service.GetRateableTeammatesAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<IReadOnlyList<RateableTeammateDto>>(new HttpRequestException())
                    : Task.FromResult<IReadOnlyList<RateableTeammateDto>>(SeedFixtures.RateableTeammates);
            });
        var pageModel = CreatePageModel(client: client);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RetryCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task BackCommand_WhenExecuted_NavigatesBack()
    {
        var navigator = Navigator();
        var pageModel = CreatePageModel(navigator: navigator);

        await pageModel.BackCommand.ExecuteAsync(null);

        navigator.Verify(service => service.GoBackAsync(), Times.Once);
    }

    [Fact]
    public void RateTeammatesPageXaml_UsesWireframeControlsFontAwesomeAndScrollableList()
    {
        var xaml = ReadXaml("RateTeammatesPage.xaml");
        var page = XDocument.Parse(xaml);

        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "BrandHeader" &&
            Attribute(element, "Title") == "Rate the match" &&
            Attribute(element, "ShowBack") == "True");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "StateView");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "CollectionView");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "BrandCard");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "Avatar");
        xaml.Should().Contain("IconToggleButtonLike");
        xaml.Should().Contain("IconToggleButtonMvp");
        xaml.Should().Contain("RatingSlider");
        xaml.Should().Contain("FontAwesomeGlyphs.Heart");
        xaml.Should().Contain("FontAwesomeGlyphs.Star");
        xaml.Should().Contain("SemanticProperties.Description=\"{Binding LikeSemanticDescription}\"");
        xaml.Should().Contain("SemanticProperties.Description=\"{Binding MvpSemanticDescription}\"");
        xaml.Should().Contain("Submit ratings");
        xaml.Should().NotContain("#");
        xaml.EnumerateRunes().Where(rune => IsEmoji(rune.Value)).Should().BeEmpty();
    }

    private static RateTeammatesPageModel CreatePageModel(
        Mock<IStatsClient>? client = null,
        Mock<IRateTeammatesNavigator>? navigator = null) =>
        new(
            (client ?? SeedStatsClient()).Object,
            (navigator ?? Navigator()).Object,
            new RateTeammatesOptions
            {
                MatchId = SeedFixtures.FeaturedMatchId,
                RaterId = SeedFixtures.CurrentPlayerId
            });

    private static Mock<IStatsClient> SeedStatsClient(
        IReadOnlyList<RateableTeammateDto>? teammates = null)
    {
        var client = new Mock<IStatsClient>();
        client.Setup(service => service.GetRateableTeammatesAsync(
                SeedFixtures.FeaturedMatchId,
                SeedFixtures.CurrentPlayerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(teammates ?? SeedFixtures.RateableTeammates);
        client.Setup(service => service.SubmitRatingsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<TeammateRatingDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClientCommandResult.Success);
        return client;
    }

    private static Mock<IRateTeammatesNavigator> Navigator()
    {
        var navigator = new Mock<IRateTeammatesNavigator>();
        navigator.Setup(service => service.GoBackAsync()).Returns(Task.CompletedTask);
        return navigator;
    }

    private static string ReadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return File.ReadAllText(path);
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

    private static bool IsEmoji(int codePoint) =>
        codePoint is (>= 0x1F000 and <= 0x1FAFF)
            or (>= 0x2600 and <= 0x26FF)
            or (>= 0x2700 and <= 0x27BF)
            or 0xFE0F
            or 0x20E3
            or (>= 0x1F1E6 and <= 0x1F1FF);
}
