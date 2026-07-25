using System.Net.Http;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using SouthBaySoccer.Contracts.Profiles;
using SouthBaySoccer.Controls;
using SouthBaySoccer.PageModels;
using SouthBaySoccer.SeedData;
using SouthBaySoccer.Services;
using SouthBaySoccer.Services.Authentication;
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
        pageModel.MatchesText.Should().Be("24");
        pageModel.GoalsText.Should().Be("12");
        pageModel.AssistsText.Should().Be("9");
        pageModel.AverageRatingText.Should().Be("7.8");
        pageModel.MvpAwardsText.Should().Be("3");
        pageModel.LikesText.Should().Be("41");
        pageModel.RecentForm.Select(item => item.Text).Should().Equal("W", "W", "D", "W", "L");
        pageModel.PendingNote.Should().Be("2 goals from Sat awaiting confirmation");
        pageModel.HasPendingNote.Should().BeTrue();
        pageModel.IsBusy.Should().BeFalse();
    }


    [Fact]
    public async Task Appearing_PlayerIdRoute_LoadsRequestedPlayerProfileAndHidesEditAction()
    {
        var player = SeedFixtures.Players[1];
        var routedProfile = new PlayerProfileDto(
            player.Id,
            player.DisplayName,
            "Forward \u00B7 #2",
            player.Initials,
            new CareerStatsDto(23, 13, 10, 8.3m, 4, 42),
            [MatchResult.Win, MatchResult.Draw],
            null);
        var profileClient = new Mock<IProfileClient>(MockBehavior.Strict);
        profileClient
            .Setup(client => client.GetProfileAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(routedProfile);
        var pageModel = CreatePageModel(profileClient);
        pageModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [ProfilePageModel.PlayerIdQueryKey] = player.Id.ToString()
        });

        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.State.Should().Be(ViewState.Content);
        pageModel.Profile.Should().Be(routedProfile);
        pageModel.GoalsText.Should().Be("13");
        pageModel.AverageRatingText.Should().Be("8.3");
        pageModel.CanEditProfile.Should().BeFalse();
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Never);
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
    public void ProfilePageXaml_AndroidSafeLayout_UsesStableThreeColumnStatGrid()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var statTiles = page.Descendants()
            .Where(element => element.Name.LocalName == "StatTile")
            .ToList();
        var statGrid = statTiles[0].Parent!;

        statTiles.Should().HaveCount(6);
        Attribute(statGrid, "ColumnDefinitions").Should().Be("*,*,*");
        Attribute(statGrid, "RowDefinitions").Should().Be("Auto,Auto");
        page.Descendants().Should().NotContain(
            element => element.Name.LocalName == "AdaptiveTrigger",
            "cross-element visual-state targets throw XamlParseException when the Android Shell creates the tab");
        page.Descendants().Should().NotContain(
            element => element.Name.LocalName == "Setter" && Attribute(element, "TargetName") != null);
        statTiles.Select(tile => Attribute(tile, "Value"))
            .Should().Equal(
                "{Binding MatchesText}",
                "{Binding WinsText}",
                "{Binding LossesText}",
                "{Binding AverageRatingText}",
                "{Binding MvpAwardsText}",
                "{Binding LikesText}");
    }

    [Fact]
    public void ProfilePageXaml_IdentityActions_ShareOneSpaceBetweenRow()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var linkedIdentity = page.Descendants()
            .Single(element =>
                element.Name.LocalName == "HorizontalStackLayout" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "LinkedIdentity"));
        var identityRow = linkedIdentity.Parent!;
        var editAction = identityRow.Elements()
            .Single(element => element.Name.LocalName == "Grid" && Attribute(element, "Grid.Column") == "1");

        Attribute(identityRow, "ColumnDefinitions").Should().Be("*,Auto");
        Attribute(identityRow, "RowDefinitions").Should().BeNull();
        Attribute(linkedIdentity, "Grid.Column").Should().Be("0");
        Attribute(editAction, "HorizontalOptions").Should().Be("End");
    }

    [Fact]
    public void ProfilePageXaml_InteractiveActions_UseSharedTouchSafeStyles()
    {
        var page = LoadXaml("ProfilePage.xaml");
        var buttons = page.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToList();

        buttons.Should().HaveCount(3);
        buttons.Select(button => Attribute(button, "Style"))
            .Should().BeEquivalentTo(
                "{StaticResource LinkButton}",
                "{StaticResource GhostButton}",
                "{StaticResource DangerButton}");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "ScrollView");
    }

    [Fact]
    public void ProfilePageXaml_EditAction_BindsToCurrentProfileOnly()
    {
        var page = LoadXaml("ProfilePage.xaml");
        page.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Grid" &&
            Attribute(element, "IsVisible") == "{Binding CanEditProfile}");
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
        client
            .Setup(service => service.GetProfileAsync(profile.PlayerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        return client;
    }

    [Fact]
    public void ApplyQueryAttributes_WithPlayerId_MarksViewingOtherPlayer()
    {
        var pageModel = CreatePageModel(new Mock<IProfileClient>());

        pageModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [ProfilePageModel.PlayerIdQueryKey] = Guid.NewGuid().ToString()
        });

        pageModel.IsViewingOtherPlayer.Should().BeTrue("a pushed detail page needs its own back button");
        pageModel.CanEditProfile.Should().BeFalse();
    }

    [Fact]
    public void ApplyQueryAttributes_WithoutPlayerId_IsTheSignedInPlayersOwnProfile()
    {
        var pageModel = CreatePageModel(new Mock<IProfileClient>());

        pageModel.ApplyQueryAttributes(new Dictionary<string, object>());

        pageModel.IsViewingOtherPlayer.Should().BeFalse();
        pageModel.CanEditProfile.Should().BeTrue();
    }

    /// <summary>
    /// Regression: viewing another player used to hijack the Profile tab ("//profile?playerId="),
    /// so the tab's cached page model kept that id and re-showed that player. A profile page with no
    /// requested player must always load the signed-in player, however many times it reappears.
    /// </summary>
    [Fact]
    public async Task Appearing_Repeatedly_WithNoRequestedPlayer_AlwaysLoadsTheCurrentPlayer()
    {
        var other = Guid.NewGuid();
        var profileClient = ProfileClientReturning(SeedFixtures.Profile);
        var pageModel = CreatePageModel(profileClient);

        await pageModel.AppearingCommand.ExecuteAsync(null);
        await pageModel.AppearingCommand.ExecuteAsync(null);

        pageModel.CanEditProfile.Should().BeTrue();
        profileClient.Verify(
            client => client.GetCurrentProfileAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        profileClient.Verify(
            client => client.GetProfileAsync(other, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Back_PopsThePushedProfileDetailPage()
    {
        var navigator = Navigator();
        var pageModel = CreatePageModel(new Mock<IProfileClient>(), navigator: navigator);

        await pageModel.BackCommand.ExecuteAsync(null);

        navigator.Verify(nav => nav.GoBackAsync(), Times.Once);
    }

    [Fact]
    public async Task SignOut_WhenConfirmed_SignsOutThroughCoordinator()
    {
        var dialog = new Mock<IUserDialogService>();
        dialog
            .Setup(service => service.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var coordinator = new Mock<IAuthenticationCoordinator>();
        var pageModel = CreatePageModel(
            new Mock<IProfileClient>(),
            authenticationCoordinator: coordinator,
            dialogService: dialog);

        await pageModel.SignOutCommand.ExecuteAsync(null);

        coordinator.Verify(c => c.SignOutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignOut_WhenCancelled_DoesNotSignOut()
    {
        var dialog = new Mock<IUserDialogService>();
        dialog
            .Setup(service => service.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var coordinator = new Mock<IAuthenticationCoordinator>();
        var pageModel = CreatePageModel(
            new Mock<IProfileClient>(),
            authenticationCoordinator: coordinator,
            dialogService: dialog);

        await pageModel.SignOutCommand.ExecuteAsync(null);

        coordinator.Verify(c => c.SignOutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProfilePageModel CreatePageModel(
        Mock<IProfileClient> profileClient,
        Mock<IProfileExternalLauncher>? launcher = null,
        Mock<IProfileNavigator>? navigator = null,
        Mock<IAuthenticationCoordinator>? authenticationCoordinator = null,
        Mock<IUserDialogService>? dialogService = null) =>
        new(
            profileClient.Object,
            (launcher ?? LauncherReturning(true)).Object,
            (navigator ?? Navigator()).Object,
            (authenticationCoordinator ?? new Mock<IAuthenticationCoordinator>()).Object,
            (dialogService ?? new Mock<IUserDialogService>()).Object);

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
