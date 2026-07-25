using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

// Shell and the root pages require a MAUI host, so these tests inspect the shipped XAML/source
// copied to the test output. Runtime tab selection remains platform-owned by the native Shell
// TabBar; the tests guard the application-owned route, template, accessibility, and root contracts.
public class AuthenticatedShellTests
{
    private static readonly string[] SampleRoutes = ["home", "design-system", "projects", "manage"];

    private static readonly string[] SamplePages =
        ["MainPage", "DesignSystemPage", "ProjectListPage", "ManageMetaPage"];

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

    private static List<XElement> ShellContents(XDocument shell) =>
        shell.Descendants().Where(element => element.Name.LocalName == "ShellContent").ToList();

    // The five bottom tabs live inside the TabBar. Other ShellContents (e.g. the blocking
    // link-group route) are declared outside it and are intentionally excluded here.
    private static List<XElement> TabBarContents(XDocument shell) =>
        shell.Descendants()
            .Single(element => element.Name.LocalName == "TabBar")
            .Elements()
            .Where(element => element.Name.LocalName == "ShellContent")
            .ToList();

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private static string? KeyOf(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value;

    [Fact]
    public void AppShell_StartupTab_IsSessionsHome()
    {
        var first = ShellContents(LoadXaml("AppShell.xaml")).First();

        Attribute(first, "Route").Should().Be("sessions");
        Attribute(first, "ContentTemplate").Should().Contain("SessionsHomePage");
    }

    [Fact]
    public void AppShell_Tabs_AreSessionsGameDayStatsPlayersProfileInOrder()
    {
        var contents = TabBarContents(LoadXaml("AppShell.xaml"));

        contents.Select(content => Attribute(content, "Route"))
            .Should().Equal("sessions", "gameday", "stats", "players", "profile");
        contents.Select(content => Attribute(content, "Title"))
            .Should().Equal("Sessions", "Game Day", "Stats", "Players", "Profile");
    }

    [Fact]
    public void AppShell_RootTabs_AreSiblingsInsideOnePersistentTabBar()
    {
        var shell = LoadXaml("AppShell.xaml");
        var tabBar = shell.Descendants().Single(element => element.Name.LocalName == "TabBar");
        var rootTabs = tabBar.Elements()
            .Where(element => element.Name.LocalName == "ShellContent")
            .ToList();

        rootTabs.Should().HaveCount(5);
        rootTabs.Select(content => Attribute(content, "ContentTemplate"))
            .Should().Equal(
                "{DataTemplate pages:SessionsHomePage}",
                "{DataTemplate pages:GameDayPage}",
                "{DataTemplate pages:StatsPage}",
                "{DataTemplate pages:PlayersPage}",
                "{DataTemplate pages:ProfilePage}");
    }

    [Fact]
    public void AppShell_Tabs_HaveStableAutomationIdsAndSemanticNames()
    {
        var contents = TabBarContents(LoadXaml("AppShell.xaml"));

        contents.Select(content => Attribute(content, "AutomationId"))
            .Should().Equal("SessionsTab", "GameDayTab", "StatsTab", "PlayersTab", "ProfileTab");
        contents.Select(content => Attribute(content, "SemanticProperties.Description"))
            .Should().Equal("Sessions tab", "Game Day tab", "Stats tab", "Players tab", "Profile tab");
    }

    [Fact]
    public void AppShell_TabIcons_UseTypedFontAwesomeSolidResources()
    {
        var styles = LoadXaml("AppStyles.xaml");
        var expectedGlyphs = new Dictionary<string, string>
        {
            ["IconSessions"] = "FontAwesomeGlyphs.CalendarDays",
            ["IconStats"] = "FontAwesomeGlyphs.Trophy",
            ["IconPlayers"] = "FontAwesomeGlyphs.Users",
            ["IconProfile"] = "FontAwesomeGlyphs.User",
        };

        foreach (var (key, glyph) in expectedGlyphs)
        {
            var resource = styles.Descendants()
                .Single(element => element.Name.LocalName == "FontImageSource" && KeyOf(element) == key);

            Attribute(resource, "FontFamily").Should().Be("FontAwesomeSolid");
            Attribute(resource, "Glyph").Should().Contain(glyph);
            resource.ToString().Should().NotContain("#");
        }
    }

    [Fact]
    public void AppShell_UsesBrandTokensForSelectedAndUnselectedTabs()
    {
        var styles = LoadXaml("BrandStyles.xaml");
        var shellStyle = styles.Descendants()
            .Single(element =>
                element.Name.LocalName == "Style" &&
                Attribute(element, "TargetType") == "Shell");
        var setters = shellStyle.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => Attribute(element, "Property")!,
                element => Attribute(element, "Value")!);

        setters["Shell.TabBarForegroundColor"].Should().Contain("BrandGreen");
        setters["Shell.TabBarUnselectedColor"].Should().Contain("BrandSage");
        shellStyle.ToString().Should().NotContain("#");
    }

    [Fact]
    public void Accessibility_TouchMinimumToken_IsFortyFourDip()
    {
        var shell = LoadXaml("AppShell.xaml");
        var tokens = LoadXaml("BrandTokens.xaml");
        var touchMinimum = tokens.Descendants()
            .Single(element => KeyOf(element) == "TouchMin");

        shell.Descendants().Should().ContainSingle(element => element.Name.LocalName == "TabBar");
        touchMinimum.Value.Should().Be("44");
    }

    [Fact]
    public void StatsRootTab_RendersImplementedLeaderboardScreen()
    {
        var page = LoadXaml("StatsPage.xaml");
        var stateView = page.Descendants()
            .Single(element => element.Name.LocalName == "StateView");

        page.ToString().Should().Contain("Leaderboard");
        page.Descendants().Should().Contain(element => element.Name.LocalName == "SegmentedControl");
        Attribute(stateView, "State").Should().Be("{Binding State}");
        Attribute(stateView, "Glyph").Should().Contain("FontAwesomeGlyphs.Trophy");
        Attribute(stateView, "GlyphFontFamily").Should().Be("FontAwesomeSolid");
    }

    [Fact]
    public void AuthenticationNavigator_AuthenticatedTransition_ReplacesWindowRoot()
    {
        var source = LoadSource("AuthenticationNavigator.cs");

        source.Should().Contain("window.Page = shell");
        // After the shell loads the navigator routes to //sessions, or to the blocking //link-group
        // step first when the player belongs to no group yet.
        source.Should().Contain("\"//sessions\"");
        source.Should().Contain("\"//link-group\"");
        source.Should().Contain("shell.GoToAsync(route)");
        source.Should().NotContain("PushAsync(");
    }

    [Fact]
    public void AuthenticationNavigator_NavigatesOnceShellIsReady_NotOnAFixedDelay()
    {
        // Item D fix: a 100ms DispatchDelayed guess is not a guarantee the Shell's handler has
        // attached on slow devices. Navigation must wait for the Shell to actually report ready
        // (Loaded), and any navigation failure must reach an error handler instead of being
        // silently swallowed by FireAndForgetSafeAsync's null-handler default.
        var source = LoadSource("AuthenticationNavigator.cs");

        source.Should().NotContain("DispatchDelayed");
        source.Should().Contain("shell.Loaded");
        source.Should().Contain("FireAndForgetSafeAsync(errorHandler)");
    }

    [Fact]
    public void AppShell_AuthenticatedStartup_ExcludesLegacySampleScaffoldingAndWelcomeBack()
    {
        var contents = ShellContents(LoadXaml("AppShell.xaml"));

        contents.Select(content => Attribute(content, "Route")).Should().NotContain(SampleRoutes);

        var templates = contents
            .Select(content => Attribute(content, "ContentTemplate") ?? string.Empty)
            .ToList();
        templates.Should().NotContain(template => template.Contains("WelcomeBackPage"));
        foreach (var samplePage in SamplePages)
        {
            templates.Should().NotContain(
                template => template.Contains(samplePage),
                $"the {samplePage} sample scaffolding must not be on the authenticated startup path");
        }
    }

    [Fact]
    public void SessionsHome_ScrollView_DoesNotContainCollectionView()
    {
        var sessionsHome = LoadXaml("SessionsHomePage.xaml");
        var scrollViews = sessionsHome.Descendants()
            .Where(element => element.Name.LocalName == "ScrollView");

        scrollViews.Should().NotContain(
            scrollView => scrollView.Descendants().Any(element => element.Name.LocalName == "CollectionView"),
            "a vertical CollectionView inside the page ScrollView can enter an unbounded Android measure loop");
    }

    [Theory]
    [InlineData("BrandCard.xaml")]
    [InlineData("StateView.xaml")]
    public void BodyContentControl_InternalVisualTree_UsesExplicitContentProperty(string fileName)
    {
        var control = LoadXaml(fileName);

        control.Root!.Elements().Single().Name.LocalName.Should().Be(
            "ContentView.Content",
            "the control's internal tree must not be assigned to its Body content property and presented inside itself");
    }

    [Fact]
    public void StateView_GlyphFontFamily_IsBoundFromTheControlContract()
    {
        var stateView = LoadXaml("StateView.xaml");
        var glyph = stateView.Descendants()
            .First(element => element.Name.LocalName == "Label" && Attribute(element, "Text")!.Contains("Glyph"));

        Attribute(glyph, "FontFamily").Should().Contain("GlyphFontFamily");
    }
}
