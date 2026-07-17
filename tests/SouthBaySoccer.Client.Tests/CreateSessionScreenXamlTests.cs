using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

// ADMIN-4 semantic + iconography contract for the Create session screen. Like the other session
// screen tests, this inspects the shipped XAML copied to the test output rather than hosting MAUI.
// It guards the Font Awesome / no-emoji invariant (INV-13), the accessible back button and publish
// action, and the scrollable layout that keeps the form uncut at large text on narrow phones.
public class CreateSessionScreenXamlTests
{
    private const string Page = "CreateSessionPage.xaml";

    private static string XamlPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", Page);
        File.Exists(path).Should().BeTrue($"the test project must copy {Page} to its output");
        return path;
    }

    private static XDocument LoadXaml() => XDocument.Load(XamlPath());

    private static string ReadXaml() => File.ReadAllText(XamlPath());

    private static IEnumerable<XElement> Elements(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    private static string? Attr(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    [Fact]
    public void CreateSessionScreen_WrapsContentInScrollView() =>
        Elements(LoadXaml(), "ScrollView").Should().NotBeEmpty();

    [Fact]
    public void CreateSessionScreen_UsesThemeTokensNotRawHexColors() =>
        ReadXaml().Should().NotContain("#", "colours must come from named light/dark tokens, not raw hex");

    [Fact]
    public void CreateSessionScreen_IconLabelsUseTypedFontAwesomeGlyphs()
    {
        var fontAwesomeLabels = Elements(LoadXaml(), "Label")
            .Where(label => (Attr(label, "FontFamily") ?? string.Empty).Contains("FontAwesome"));

        fontAwesomeLabels.Should().NotBeEmpty("the screen renders pictograms");
        fontAwesomeLabels.Should().OnlyContain(
            label => (Attr(label, "Text") ?? string.Empty).StartsWith("{x:Static"),
            "Font Awesome glyphs must reference the typed catalog, not inline Unicode literals");
    }

    [Fact]
    public void CreateSessionScreen_ContainsNoUnicodeEmoji()
    {
        var offending = ReadXaml()
            .EnumerateRunes()
            .Where(rune => IsEmoji(rune.Value))
            .Select(rune => rune.Value)
            .ToList();

        offending.Should().BeEmpty("INV-13 forbids Unicode emoji; pictograms use Font Awesome glyphs");
    }

    [Fact]
    public void CreateSessionScreen_HeaderProvidesAccessibleBackButton()
    {
        var header = Elements(LoadXaml(), "BrandHeader").Single();

        Attr(header, "ShowBack").Should().Be("True");
        Attr(header, "BackCommand").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateSessionScreen_PublishActionExposesSemanticDescription() =>
        ReadXaml().Should().Contain("SemanticProperties.Description=\"Publish or update session\"");

    [Fact]
    public void CreateSessionScreen_HeaderMatchesAdminSessionWireframe()
    {
        var header = Elements(LoadXaml(), "BrandHeader").Single();

        Attr(header, "Title").Should().Be("Create session");
        Attr(header, "Subtitle").Should().Be("Admin only");
        Attr(header, "ShowBack").Should().Be("True");
    }

    [Fact]
    public void CreateSessionScreen_ContainsAdminSessionWireframeSectionsAndLabels()
    {
        var xaml = ReadXaml();

        xaml.Should().Contain("Create a session, preview it, then publish it to the team so players can RSVP.");
        xaml.Should().Contain("CREATED SESSIONS");
        xaml.Should().Contain("Edit");
        xaml.Should().Contain("GAME DATE AND TIME");
        xaml.Should().Contain("GAME DATE");
        xaml.Should().Contain("START TIME");
        xaml.Should().Contain("RSVP CLOSE");
        xaml.Should().Contain("CHECK-IN OPENS");
        xaml.Should().Contain("CHECK-IN CLOSES");
        xaml.Should().Contain("LOCATION");
        xaml.Should().Contain("Search venues");
        xaml.Should().Contain("Map");
        xaml.Should().Contain("GAME SETUP");
        xaml.Should().Contain("PLAYER PREVIEW");
        xaml.Should().Contain("Ready for RSVP");
        xaml.Should().Contain("PrimaryActionText");
    }

    [Fact]
    public void CreateSessionScreen_UsesSharedControlsForAdminSessionStructure()
    {
        var doc = LoadXaml();

        Elements(doc, "BrandHeader").Should().ContainSingle();
        Elements(doc, "BrandCard").Should().HaveCountGreaterThanOrEqualTo(4);
        Elements(doc, "SegmentedControl").Should().HaveCount(2);
        Elements(doc, "CounterStepper").Should().ContainSingle();
        Elements(doc, "StateView").Should().ContainSingle("admin denial and offline states use shared state UI");
    }

    [Fact]
    public void CreateSessionScreen_CheckInWindowIsAdjustableInsteadOfPreviewOnly()
    {
        var xaml = ReadXaml();

        xaml.Should().Contain("CHECK-IN OPENS");
        xaml.Should().Contain("CHECK-IN CLOSES");
        Elements(LoadXaml(), "TimePicker")
            .Select(element => Attr(element, "SemanticProperties.Description"))
            .Should()
            .Contain("Check-in opens")
            .And.Contain("Check-in closes");
    }

    [Fact]
    public void CreateSessionScreen_VenueSearchUsesCodeBehindTextChangedHandler()
    {
        var xaml = ReadXaml();

        xaml.Should().Contain("x:Name=\"VenueSearchEntry\"");
        xaml.Should().Contain("TextChanged=\"OnVenueSearchTextChanged\"");
        xaml.Should().NotContain("Command=\"{Binding SearchVenuesCommand}\"", "Android should not depend on toolkit behavior command binding for Entry text changes");
    }

    [Fact]
    public void CreateSessionScreen_DoesNotUseToolkitEventBehaviorForAppearing()
    {
        ReadXaml().Should().NotContain(
            "EventToCommandBehavior",
            "Create Session uses code-behind lifecycle wiring to avoid Android behavior binding crashes");
    }

    [Fact]
    public void CreateSessionScreen_RsvpDeadlineIsEditable()
    {
        Elements(LoadXaml(), "TimePicker")
            .Select(element => Attr(element, "SemanticProperties.Description"))
            .Should()
            .Contain("RSVP close time");
    }

    [Fact]
    public void CreateSessionScreen_DoesNotDeclareUnusedToolkitNamespace() =>
        ReadXaml().Should().NotContain(
            "xmlns:toolkit",
            "the toolkit namespace is unused now that Entry text changes are code-behind wired");

    [Fact]
    public void CreateSessionScreen_AddVenueButtonVisibilityTracksQueryNoveltyNotCreationState()
    {
        var xaml = ReadXaml();

        xaml.Should().Contain("IsVisible=\"{Binding IsVenueQueryNovel}\"",
            "the add-venue button must stay visible while IsCreatingVenue flips so it does not vanish under the admin's finger");
        xaml.Should().NotContain("IsVisible=\"{Binding CanCreateVenue}\"");
    }

    [Fact]
    public void CreateSessionScreen_AddVenueButtonSemanticDescriptionReflectsDynamicText() =>
        ReadXaml().Should().Contain("SemanticProperties.Description=\"{Binding CreateVenueActionText}\"");

    private static bool IsEmoji(int codePoint) =>
        codePoint is (>= 0x1F000 and <= 0x1FAFF)
            or (>= 0x2600 and <= 0x26FF)
            or (>= 0x2700 and <= 0x27BF)
            or 0xFE0F
            or 0x20E3
            or (>= 0x1F1E6 and <= 0x1F1FF);
}

