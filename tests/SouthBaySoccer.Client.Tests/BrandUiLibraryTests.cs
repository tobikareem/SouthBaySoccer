using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

// M11.0c: the reusable-library extensions (BrandHeader/PlayerRow LeadingContent slots and the
// IconButton / IconToggleButton / MetadataChip / RatingSlider shared styles) are XAML, so they
// cannot be instantiated without a MAUI host. These tests inspect the shipped XAML — copied to the
// test output by the project file — to guard the accessibility, touch-target, token, and
// bound-visual-state contracts in _specs/client-ui.md §6.1 and §8.
public class BrandUiLibraryTests
{
    private static XDocument LoadXaml(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", "Xaml", fileName);
        File.Exists(path).Should().BeTrue($"the test project must copy {fileName} to its output");
        return XDocument.Load(path);
    }

    private static XElement Style(XDocument doc, string key) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "Style")
            .FirstOrDefault(e => KeyOf(e) == key)
            ?? throw new Xunit.Sdk.XunitException($"Style '{key}' was not found.");

    private static XElement Template(XDocument doc, string key) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "ControlTemplate")
            .FirstOrDefault(e => KeyOf(e) == key)
            ?? throw new Xunit.Sdk.XunitException($"ControlTemplate '{key}' was not found.");

    private static string? KeyOf(XElement element) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;

    private static IEnumerable<XElement> Setters(XElement style) =>
        style.Elements().Where(e => e.Name.LocalName == "Setter");

    private static string? SetterValue(XElement style, string property) =>
        Setters(style)
            .FirstOrDefault(s => s.Attribute("Property")?.Value == property)
            ?.Attribute("Value")?.Value;

    [Theory]
    [InlineData("IconButton")]
    [InlineData("IconToggleButton")]
    [InlineData("IconToggleButtonLike")]
    [InlineData("IconToggleButtonMvp")]
    [InlineData("MetadataChip")]
    [InlineData("RatingSlider")]
    public void BrandStyles_DefinesM110cSharedStyle(string key)
    {
        var brandStyles = LoadXaml("BrandStyles.xaml");

        var act = () => Style(brandStyles, key);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("IconButton")]
    [InlineData("IconToggleButton")]
    public void InteractiveIconStyle_HasAtLeast44DipTouchTarget(string key)
    {
        var style = Style(LoadXaml("BrandStyles.xaml"), key);

        // The 44-dip minimum (TouchMin) is expressed as a token, never a magic number.
        SetterValue(style, "MinimumHeightRequest").Should().Be("{StaticResource TouchMin}");
        SetterValue(style, "MinimumWidthRequest").Should().Be("{StaticResource TouchMin}");
    }

    [Fact]
    public void IconToggleButton_DrivesAppearanceFromBoundCheckedState()
    {
        var doc = LoadXaml("BrandStyles.xaml");

        // Appearance is template-driven: the style points at a ControlTemplate...
        SetterValue(Style(doc, "IconToggleButton"), "ControlTemplate")
            .Should().Be("{StaticResource IconToggleTemplate}");

        // ...and that template recolors the glyph from the bound IsChecked state via CheckedStates.
        var template = Template(doc, "IconToggleTemplate");
        var checkedStates = template.Descendants()
            .Where(e => e.Name.LocalName == "VisualStateGroup")
            .FirstOrDefault(e => e.Attributes()
                .Any(a => a.Name.LocalName == "Name" && a.Value == "CheckedStates"));
        checkedStates.Should().NotBeNull("the on/off state is the bound IsChecked state, not code-behind");

        var stateNames = checkedStates!.Descendants()
            .Where(e => e.Name.LocalName == "VisualState")
            .Select(e => e.Attributes().First(a => a.Name.LocalName == "Name").Value);
        stateNames.Should().Contain(new[] { "Unchecked", "Checked" });

        // Each state recolors the template-root glyph's own TextColor (the SourceGen-safe,
        // reliable templated-RadioButton pattern) rather than relying on TemplateBinding.
        checkedStates!.Descendants()
            .Where(e => e.Name.LocalName == "Setter")
            .Should().OnlyContain(s => s.Attribute("Property")!.Value == "TextColor");
    }

    [Theory]
    [InlineData("IconToggleButtonLike", "IconToggleTemplateLike", "DangerLight")]
    [InlineData("IconToggleButtonMvp", "IconToggleTemplateMvp", "WarningLight")]
    public void IconToggleVariant_SetsSemanticOnColor(string styleKey, string templateKey, string expectedToken)
    {
        var doc = LoadXaml("BrandStyles.xaml");

        SetterValue(Style(doc, styleKey), "ControlTemplate")
            .Should().Be($"{{StaticResource {templateKey}}}");

        var checkedState = Template(doc, templateKey).Descendants()
            .Where(e => e.Name.LocalName == "VisualState")
            .First(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "Checked"));
        checkedState.ToString().Should().Contain(expectedToken);
    }

    [Theory]
    [InlineData("IconToggleTemplate")]
    [InlineData("IconToggleTemplateLike")]
    [InlineData("IconToggleTemplateMvp")]
    public void IconToggleTemplate_ContainsNoRawHexColors(string key)
    {
        Template(LoadXaml("BrandStyles.xaml"), key).ToString()
            .Should().NotContain("#", "template colors must come from named tokens, not raw hex");
    }

    [Fact]
    public void RatingSlider_ConstrainsRangeToZeroThroughTen()
    {
        var style = Style(LoadXaml("BrandStyles.xaml"), "RatingSlider");

        style.Attribute("TargetType")!.Value.Should().Be("Slider");
        SetterValue(style, "Minimum").Should().Be("0");
        SetterValue(style, "Maximum").Should().Be("10");
    }

    [Fact]
    public void MetadataChip_IsATokenizedBorderSurface()
    {
        var style = Style(LoadXaml("BrandStyles.xaml"), "MetadataChip");

        style.Attribute("TargetType")!.Value.Should().Be("Border");
        SetterValue(style, "BackgroundColor").Should().Contain("BrandMist");
    }

    [Theory]
    [InlineData("IconButton")]
    [InlineData("IconToggleButton")]
    [InlineData("IconToggleButtonLike")]
    [InlineData("IconToggleButtonMvp")]
    [InlineData("MetadataChip")]
    [InlineData("RatingSlider")]
    public void M110cStyle_ContainsNoRawHexColors(string key)
    {
        var style = Style(LoadXaml("BrandStyles.xaml"), key);

        style.ToString().Should().NotContain("#", "colors must come from named tokens, not raw hex");
    }

    [Fact]
    public void BrandHeader_ExposesLeadingContentSlot()
    {
        var header = LoadXaml("BrandHeader.xaml");

        header.ToString().Should().Contain("LeadingContent");
    }

    [Fact]
    public void BrandHeader_KeepsAccessibleBackButton()
    {
        var header = LoadXaml("BrandHeader.xaml");

        var back = header.Descendants()
            .First(e => e.Name.LocalName == "Button");
        back.ToString().Should().Contain("SemanticProperties.Description=\"Back\"");
        back.Attribute("HeightRequest")!.Value.Should().Be("{StaticResource TouchMin}");
    }

    [Fact]
    public void PlayerRow_ExposesLeadingContentSlot()
    {
        var row = LoadXaml("PlayerRow.xaml");

        row.ToString().Should().Contain("LeadingContent");
    }

    [Fact]
    public void AppShell_HomeRoute_UsesStableDashboardInsteadOfUiLibraryShowcase()
    {
        var shell = LoadXaml("AppShell.xaml");
        var home = shell.Descendants()
            .First(element =>
                element.Name.LocalName == "ShellContent"
                && element.Attribute("Route")?.Value == "home");

        home.Attribute("ContentTemplate")!.Value.Should().Contain("MainPage");
        home.Attribute("ContentTemplate")!.Value.Should().NotContain("DesignSystemPage");
    }

    [Fact]
    public void AppShell_UiLibrary_RemainsAvailableAsSecondaryRoute()
    {
        var shell = LoadXaml("AppShell.xaml");
        var uiLibrary = shell.Descendants()
            .First(element =>
                element.Name.LocalName == "ShellContent"
                && element.Attribute("Route")?.Value == "design-system");

        uiLibrary.Attribute("ContentTemplate")!.Value.Should().Contain("DesignSystemPage");
    }
}
