using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services.Clients;

namespace SouthBaySoccer.Client.Tests;

public class ProfilePageModelTests
{
    [Fact]
    public async Task Appearing_SeedProfile_LoadsIdentityStatsFormAndPendingNote()
    {
        var profileClient = ProfileClientReturning(SeedFixtures.Profile);
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Profile.Should().Be(SeedFixtures.Profile);
        pageModel.Profile!.CareerStats.Should().Be(new CareerStatsDto(24, 12, 9, 7.8m, 3, 41));
        pageModel.RecentForm.Select(item => item.Text).Should().Equal("W", "W", "D", "W", "L");
        pageModel.PendingNote.Should().Be("2 goals from Sat awaiting confirmation");
        pageModel.HasPendingNote.Should().BeTrue();
        pageModel.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_RequestInFlight_ShowsLoadingState()
    {
        var completion = new TaskCompletionSource<PlayerProfileDto?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .Returns(completion.Task);
        var pageModel = CreatePageModel(profileClient);

        var loadTask = pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Loading);
        pageModel.IsBusy.Should().BeTrue();

        completion.SetResult(SeedFixtures.Profile);
        await loadTask;
    }

    [Fact]
    public async Task Appearing_ZeroStats_LoadsZeroStateWithoutInventingValues()
    {
        var zeroProfile = SeedFixtures.Profile with
        {
            CareerStats = new CareerStatsDto(0, 0, 0, 0m, 0, 0),
            RecentForm = [],
            PendingConfirmationNote = null
        };
        var pageModel = CreatePageModel(ProfileClientReturning(zeroProfile));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Profile!.CareerStats.Should().Be(new CareerStatsDto(0, 0, 0, 0m, 0, 0));
        pageModel.RecentForm.Should().BeEmpty();
        pageModel.HasPendingNote.Should().BeFalse();
    }

    [Fact]
    public async Task Appearing_ProfileNotFound_ShowsEmptyStateAndClearsProfile()
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .SetupSequence(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedFixtures.Profile)
            .ReturnsAsync((PlayerProfileDto?)null);
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RefreshCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Empty);
        pageModel.Profile.Should().BeNull();
        pageModel.PendingNote.Should().BeEmpty();
        pageModel.StateTitle.Should().Be(ProfilePageModel.EmptyTitle);
    }

    [Fact]
    public async Task Appearing_HttpFailure_ShowsOfflineState()
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Offline);
        pageModel.StateTitle.Should().Be(ProfilePageModel.OfflineTitle);
        pageModel.Profile.Should().BeNull();
    }

    [Fact]
    public async Task Appearing_UnexpectedFailure_ShowsErrorState()
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .Setup(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Error);
        pageModel.StateTitle.Should().Be(ProfilePageModel.ErrorTitle);
        pageModel.Profile.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_AfterOffline_ReRequestsAndRecoversToContent()
    {
        var profileClient = new Mock<IProfileClient>();
        profileClient
            .SetupSequence(client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"))
            .ReturnsAsync(SeedFixtures.Profile);
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.RefreshCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Appearing_EmptyPendingNote_HidesPendingNote()
    {
        var profile = SeedFixtures.Profile with { PendingConfirmationNote = null };
        var pageModel = CreatePageModel(ProfileClientReturning(profile));

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.PendingNote.Should().BeEmpty();
        pageModel.HasPendingNote.Should().BeFalse();
    }

    [Fact]
    public async Task EditOnPickupPal_LaunchSucceeds_DoesNotReloadOrMutateProfile()
    {
        var profileClient = ProfileClientReturning(SeedFixtures.Profile);
        var launcher = new Mock<IProfileExternalLauncher>();
        launcher
            .Setup(service => service.OpenAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var pageModel = CreatePageModel(profileClient, launcher);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        var profileBeforeEdit = pageModel.Profile;
        await pageModel.EditOnPickupPalCommand.ExecuteAsync(null);

        launcher.Verify(
            service => service.OpenAccountAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        pageModel.Profile.Should().BeSameAs(profileBeforeEdit);
        pageModel.ActionMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task EditOnPickupPal_LaunchFails_ShowsRecoverableMessage()
    {
        var launcher = new Mock<IProfileExternalLauncher>();
        launcher
            .Setup(service => service.OpenAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var pageModel = CreatePageModel(new Mock<IProfileClient>(MockBehavior.Strict), launcher);

        await pageModel.EditOnPickupPalCommand.ExecuteAsync(null);

        pageModel.ActionMessage.Should().Be(ProfilePageModel.ExternalLaunchError);
        pageModel.HasActionMessage.Should().BeTrue();
    }

    [Fact]
    public async Task OpenLeaderboard_Invoked_NavigatesThroughProfileNavigator()
    {
        var navigator = new Mock<IProfileNavigator>();
        navigator.Setup(service => service.OpenLeaderboardAsync()).Returns(Task.CompletedTask);
        var pageModel = CreatePageModel(
            new Mock<IProfileClient>(MockBehavior.Strict),
            navigator: navigator);

        await pageModel.OpenLeaderboardCommand.ExecuteAsync(null);

        navigator.Verify(service => service.OpenLeaderboardAsync(), Times.Once);
    }

    [Fact]
    public void ProfilePageXaml_Icons_UseTypedFontAwesomeGlyphsAndSemanticNames()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var xml = page.ToString();

        xml.Should().Contain("FontAwesomeGlyphs.WhatsApp");
        xml.Should().Contain("FontAwesomeGlyphs.ArrowUpRightFromSquare");
        xml.Should().Contain("FontAwesomeGlyphs.Clock");
        xml.Should().NotContain("&#x");

        FontAwesomeLabel(page, "FontAwesomeGlyphs.WhatsApp")
            .Should().Match(element =>
                Attribute(element, "FontFamily") == "FontAwesomeBrands" &&
                Attribute(element, "SemanticProperties.Description") == "WhatsApp");
        FontAwesomeLabel(page, "FontAwesomeGlyphs.Clock")
            .Should().Match(element =>
                Attribute(element, "FontFamily") == "FontAwesomeSolid" &&
                Attribute(element, "SemanticProperties.Description") == "Pending confirmation");
    }

    [Fact]
    public void ProfilePageXaml_PendingVisibility_BindsBooleanProperty()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var pendingGrid = page.Descendants()
            .Single(element =>
                element.Name.LocalName == "Grid" &&
                Attribute(element, "IsVisible") == "{Binding HasPendingNote}");

        pendingGrid.Should().NotBeNull();
        page.ToString().Should().NotContain("InvertedBoolConverter");
    }

    [Fact]
    public void ProfilePageXaml_NarrowAndWideStates_ReflowIdentityAndStatTiles()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var adaptiveTriggers = page.Descendants()
            .Where(element => element.Name.LocalName == "AdaptiveTrigger")
            .ToList();
        var setters = page.Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .ToList();

        adaptiveTriggers.Should().HaveCountGreaterThanOrEqualTo(2);
        adaptiveTriggers.Should().OnlyContain(
            trigger => Attribute(trigger, "MinWindowWidth") == "360");
        setters.Should().Contain(
            setter =>
                Attribute(setter, "TargetName") == "StatGrid" &&
                Attribute(setter, "Property") == "Grid.ColumnDefinitions" &&
                Attribute(setter, "Value") == "*,*,*");
        setters.Should().Contain(
            setter =>
                Attribute(setter, "TargetName") == "EditAction" &&
                Attribute(setter, "Property") == "Grid.Column" &&
                Attribute(setter, "Value") == "1");
    }

    [Fact]
    public void ProfilePageXaml_InteractiveActions_UseSharedTouchSafeStyles()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var buttons = page.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToList();

        buttons.Should().HaveCount(2);
        buttons.Select(button => Attribute(button, "Style"))
            .Should().BeEquivalentTo(
                "{StaticResource LinkButton}",
                "{StaticResource GhostButton}");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "ScrollView");
    }

    [Fact]
    public void ProfileNavigator_LeaderboardDestination_UsesExistingStatsRootRoute()
    {
        var shell = LoadXaml("AppShell.xaml");
        var routes = shell.Descendants()
            .Where(element => element.Name.LocalName == "ShellContent")
            .Select(element => Attribute(element, "Route"));
        var navigationSource = LoadSource("ProfileFeatureServiceCollectionExtensions.cs");

        routes.Should().Contain("stats");
        routes.Should().NotContain("leaderboard");
        navigationSource.Should().Contain("GoToAsync(\"//stats\")");
        navigationSource.Should().NotContain("RegisterRoute");
    }

    private static Mock<IProfileClient> ProfileClientReturning(PlayerProfileDto profile)
    {
        var client = new Mock<IProfileClient>();
        client
            .Setup(service => service.GetCurrentProfileAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return client;
    }

    private static ProfilePageModel CreatePageModel(
        Mock<IProfileClient> profileClient,
        Mock<IProfileExternalLauncher>? launcher = null,
        Mock<IProfileNavigator>? navigator = null) =>
        new(
            profileClient.Object,
            (launcher ?? LauncherReturning(true)).Object,
            (navigator ?? Navigator()).Object);

    private static Mock<IProfileExternalLauncher> LauncherReturning(bool result)
    {
        var launcher = new Mock<IProfileExternalLauncher>();
        launcher
            .Setup(service => service.OpenAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return launcher;
    }

    private static Mock<IProfileNavigator> Navigator()
    {
        var navigator = new Mock<IProfileNavigator>();
        navigator.Setup(service => service.OpenLeaderboardAsync()).Returns(Task.CompletedTask);
        return navigator;
    }

    private static XDocument LoadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return XDocument.Load(path);
    }

    private static string LoadSource(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Source", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return File.ReadAllText(path);
    }

    private static XElement FontAwesomeLabel(XDocument page, string glyphName) =>
        page.Descendants().Single(
            element =>
                element.Name.LocalName == "Label" &&
                Attribute(element, "Text")?.Contains(glyphName) == true);

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
