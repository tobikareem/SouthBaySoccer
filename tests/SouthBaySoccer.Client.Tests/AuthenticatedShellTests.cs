using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

// The authenticated shell (AppShell.xaml) is XAML, so it cannot be instantiated without a MAUI host.
// These tests inspect the shipped XAML — copied to the test output by the project file — to guard the
// post-sign-in navigation contract: a Sessions/Stats/Profile TabBar with Sessions as the initial tab,
// and no legacy sample scaffolding on the authenticated startup path. (SES-6 / RSVP-8.)
public class AuthenticatedShellTests
{
    private static readonly string[] SampleRoutes = ["home", "design-system", "projects", "manage"];

    private static readonly string[] SamplePages =
        ["MainPage", "DesignSystemPage", "ProjectListPage", "ManageMetaPage"];

    private static XDocument LoadShell()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", "AppShell.xaml");
        File.Exists(path).Should().BeTrue("the test project must copy AppShell.xaml to its output");
        return XDocument.Load(path);
    }

    private static XDocument LoadSessionsHome()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", "SessionsHomePage.xaml");
        File.Exists(path).Should().BeTrue("the test project must copy SessionsHomePage.xaml to its output");
        return XDocument.Load(path);
    }

    private static List<XElement> ShellContents(XDocument shell) =>
        shell.Descendants().Where(e => e.Name.LocalName == "ShellContent").ToList();

    private static string? Route(XElement shellContent) =>
        shellContent.Attribute("Route")?.Value;

    [Fact]
    public void AppShell_StartupTab_IsSessionsHome()
    {
        var first = ShellContents(LoadShell()).First();

        Route(first).Should().Be("sessions");
        first.Attribute("ContentTemplate")!.Value.Should().Contain("SessionsHomePage");
    }

    [Fact]
    public void AppShell_Tabs_AreSessionsStatsProfileInOrder()
    {
        var routes = ShellContents(LoadShell()).Select(Route).ToList();

        routes.Should().Equal("sessions", "stats", "profile");
    }

    [Fact]
    public void AppShell_Tabs_AreDeclaredInsideABottomTabBar()
    {
        var shell = LoadShell();

        var tabBar = shell.Descendants().FirstOrDefault(e => e.Name.LocalName == "TabBar");
        tabBar.Should().NotBeNull("the wireframe specifies a bottom TabBar");
        tabBar!.Descendants().Count(e => e.Name.LocalName == "ShellContent").Should().Be(3);
    }

    [Fact]
    public void AppShell_AuthenticatedStartup_ExcludesLegacySampleScaffolding()
    {
        var shell = LoadShell();
        var contents = ShellContents(shell);

        contents.Select(Route).Should().NotContain(SampleRoutes);

        var templates = contents
            .Select(content => content.Attribute("ContentTemplate")?.Value ?? string.Empty)
            .ToList();
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
        var sessionsHome = LoadSessionsHome();
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
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        var control = XDocument.Load(path);

        control.Root!.Elements().Single().Name.LocalName.Should().Be(
            "ContentView.Content",
            "the control's internal tree must not be assigned to its Body content property and presented inside itself");
    }
}
