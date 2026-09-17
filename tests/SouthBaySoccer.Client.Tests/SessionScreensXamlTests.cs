using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

// SES-6 / RSVP-8 semantic + responsive contract. The session screens are XAML, so — like the
// reusable-control library tests — these inspect the shipped XAML copied to the test output rather
// than instantiating a MAUI host. They guard the Font Awesome / no-emoji iconography invariant
// (INV-13), screen-reader descriptions on interactive icons, theme-token colour usage (so light and
// dark both resolve), and the scrollable layout that keeps content uncut at large text on narrow
// phones (NFR-Accessibility).
public class SessionScreensXamlTests
{
    private const string HomePage = "SessionsHomePage.xaml";
    private const string DetailPage = "SessionDetailPage.xaml";

    private static string XamlPath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return path;
    }

    private static XDocument LoadXaml(string fileName) => XDocument.Load(XamlPath(fileName));

    private static string ReadXaml(string fileName) => File.ReadAllText(XamlPath(fileName));

    private static IEnumerable<XElement> Elements(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    private static string? Attr(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    // --- Responsive layout: content scrolls so it is never clipped at large text / narrow width ---

    [Theory]
    [InlineData(HomePage)]
    [InlineData(DetailPage)]
    public void SessionScreen_WrapsContentInScrollView(string fileName)
    {
        Elements(LoadXaml(fileName), "ScrollView")
            .Should().NotBeEmpty("the screen must stay vertically scrollable so content is never clipped");
    }

    // --- Light/dark: colours come from theme tokens, never raw hex baked for a single theme ---

    [Theory]
    [InlineData(HomePage)]
    [InlineData(DetailPage)]
    public void SessionScreen_UsesThemeTokensNotRawHexColors(string fileName)
    {
        ReadXaml(fileName)
            .Should().NotContain("#", "colours must come from named light/dark tokens, not raw hex");
    }

    // --- INV-13: every pictogram is a typed Font Awesome glyph, never an inline Unicode emoji ---

    [Theory]
    [InlineData(HomePage)]
    [InlineData(DetailPage)]
    public void SessionScreen_IconLabelsUseTypedFontAwesomeGlyphs(string fileName)
    {
        var doc = LoadXaml(fileName);

        var fontAwesomeLabels = Elements(doc, "Label")
            .Where(label => (Attr(label, "FontFamily") ?? string.Empty).Contains("FontAwesome"));

        fontAwesomeLabels.Should().NotBeEmpty("the session screens render pictograms");
        fontAwesomeLabels.Should().OnlyContain(
            label => (Attr(label, "Text") ?? string.Empty).StartsWith("{x:Static"),
            "Font Awesome glyphs must reference the typed catalog, not inline Unicode literals");
    }

    [Theory]
    [InlineData(HomePage)]
    [InlineData(DetailPage)]
    public void SessionScreen_ContainsNoUnicodeEmoji(string fileName)
    {
        var offending = ReadXaml(fileName)
            .EnumerateRunes()
            .Where(rune => IsEmoji(rune.Value))
            .Select(rune => rune.Value)
            .ToList();

        offending.Should().BeEmpty("INV-13 forbids Unicode emoji; pictograms use Font Awesome glyphs");
    }

    // --- NFR-Accessibility: informational/interactive icons carry screen-reader descriptions ---

    [Fact]
    public void SessionsHomePage_HidesDeadAnnouncementAndBroadcastEntries()
    {
        // The bell and Broadcast buttons only popped a "Coming soon" alert (real navigation is
        // disabled pending the iOS watchdog fix). They stay hidden until they navigate for real.
        var xaml = ReadXaml(HomePage);

        xaml.Should().NotContain("FontAwesomeGlyphs.Bell");
        xaml.Should().NotContain("OpenAnnouncementsCommand");
        xaml.Should().NotContain("OpenBroadcastCommand");
    }

    [Fact]
    public void SessionsHomePage_RendersWireframeClickAffordances()
    {
        var xaml = ReadXaml(HomePage);

        xaml.Should().Contain("FontAwesomeGlyphs.CircleCheck");
        xaml.Should().Contain("View details");
        xaml.Should().Contain("FontAwesomeGlyphs.ArrowRight");
        xaml.Should().Contain("FontAwesomeGlyphs.ChartColumn");
        xaml.Should().Contain("FontAwesomeGlyphs.ChevronRight");
    }

    [Fact]
    public void SessionsHomePage_SessionCardsAnnounceDisplayTitleAndStatus()
    {
        var sessionCards = Elements(LoadXaml(HomePage), "BrandCard")
            .Where(card => card
                .Descendants()
                .Where(element => element.Name.LocalName == "TapGestureRecognizer")
                .Select(element => Attr(element, "Command"))
                .Any(command => command?.Contains("ViewSessionDetailCommand", StringComparison.Ordinal) == true))
            .ToArray();

        sessionCards.Should().HaveCount(2);
        sessionCards.Should().OnlyContain(card =>
            (Attr(card, "SemanticProperties.Description") ?? string.Empty)
                .Contains("CardSemanticDescription", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionsHomePage_StatsCard_ShowsForAnyPromptAndNavigatesThroughPageModel()
    {
        var xaml = ReadXaml(HomePage);

        xaml.Should().Contain("SemanticProperties.Description=\"{Binding StatsPromptSemanticDescription}\"");
        xaml.Should().Contain("SemanticProperties.Hint=\"{Binding StatsPromptSemanticHint}\"");
        xaml.Should().Contain("Command=\"{Binding OpenStatsCommand}\"");
        // The card is shown whenever the server sends a prompt - a submit target or a claim - so an
        // unlinked player is prompted too, rather than the card only appearing for a resolved match.
        xaml.Should().Contain("IsVisible=\"{Binding HasStatsPrompt}\"");
    }

    [Fact]
    public void SessionDetailPage_InformationalIconsExposeSemanticDescriptions()
    {
        var xaml = ReadXaml(DetailPage);

        xaml.Should().Contain("SemanticProperties.Description=\"Date and time\"");
        xaml.Should().Contain("SemanticProperties.Description=\"Location\"");
    }

    [Fact]
    public void SessionDetailPage_MapAffordanceExposesSemanticDescription()
    {
        ReadXaml(DetailPage)
            .Should().Contain("SemanticProperties.Description=\"Open the venue in maps\"");
    }

    [Fact]
    public void SessionDetailPage_PrimaryRsvpActionExposesSemanticDescription()
    {
        // The button's accessible description tracks the toggling RSVP label.
        ReadXaml(DetailPage)
            .Should().Contain("SemanticProperties.Description=\"{Binding RsvpButtonText}\"");
    }

    [Fact]
    public void SessionDetailPage_HeaderProvidesAccessibleBackButton()
    {
        var header = Elements(LoadXaml(DetailPage), "BrandHeader").Single();

        Attr(header, "ShowBack").Should().Be("True");
        Attr(header, "BackCommand").Should().NotBeNullOrEmpty();
    }

    // --- RSVP-8: the going roster collapses to a preview above a "+ N more going" affordance ---

    [Fact]
    public void SessionDetailPage_RendersCollapsedGoingRosterAffordance()
    {
        var xaml = ReadXaml(DetailPage);

        xaml.Should().Contain("BindableLayout.ItemsSource=\"{Binding GoingPreview}\"",
            "the going list binds the collapsed preview, not the full roster");
        xaml.Should().Contain("{Binding MoreGoingLabel}",
            "the \"+ N more going\" affordance surfaces the hidden going players");
        xaml.Should().Contain("{Binding HasMoreGoing}",
            "the affordance is hidden when the roster fits within the preview");
    }

    private static bool IsEmoji(int codePoint) =>
        codePoint is (>= 0x1F000 and <= 0x1FAFF) // pictographs, emoticons, transport, supplemental
            or (>= 0x2600 and <= 0x26FF)          // miscellaneous symbols
            or (>= 0x2700 and <= 0x27BF)          // dingbats
            or 0xFE0F                             // emoji variation selector
            or 0x20E3                             // combining enclosing keycap
            or (>= 0x1F1E6 and <= 0x1F1FF);       // regional indicators
}
