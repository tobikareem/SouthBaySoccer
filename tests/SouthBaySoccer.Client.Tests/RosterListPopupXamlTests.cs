using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

public sealed class RosterListPopupXamlTests
{
    [Fact]
    public void TeamTallyRow_UsesOneAccessibleTextNodeAndIndependentActionButtons()
    {
        var document = LoadXaml("XamlAll", "Controls", "RosterListPopup.xaml");
        var nameLabel = document.Descendants()
            .Single(element => Attribute(element, "Text") == "{Binding DisplayName}");
        var tallyLabel = document.Descendants()
            .Single(element => Attribute(element, "Text") == "{Binding StatusLabel}");
        var rowGrid = nameLabel.Ancestors().First(element => element.Name.LocalName == "Grid");

        Attribute(nameLabel, "SemanticProperties.Description").Should().Be("{Binding SemanticDescription}");
        Attribute(tallyLabel, "AutomationProperties.IsInAccessibleTree").Should().Be("False");
        Attribute(rowGrid, "SemanticProperties.Description").Should().BeNull(
            "the layout must not add a second focus stop before its action button");
        document.Descendants().Where(element => element.Name.LocalName == "Button")
            .Should().OnlyContain(button => Attribute(button, "SemanticProperties.Description") != null);
    }

    [Fact]
    public void TeamTallyLabel_UsesPlatformEmojiFonts()
    {
        var popup = LoadXaml("XamlAll", "Controls", "RosterListPopup.xaml");
        var tallyLabel = popup.Descendants()
            .Single(element => Attribute(element, "Text") == "{Binding StatusLabel}");
        Attribute(tallyLabel, "Style").Should().Be("{StaticResource TextEmojiCaption}");

        var styles = LoadXaml("Xaml", "BrandStyles.xaml");
        var emojiFont = styles.Descendants()
            .Single(element => Attribute(element, "Key") == "EmojiFontFamily");
        emojiFont.Elements().Select(element => Attribute(element, "Platform"))
            .Should().BeEquivalentTo("Android", "iOS,MacCatalyst", "WinUI");
        emojiFont.Elements().Select(element => Attribute(element, "Value"))
            .Should().BeEquivalentTo("sans-serif", "Apple Color Emoji", "Segoe UI Emoji");
        var emojiStyle = styles.Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && Attribute(element, "Key") == "TextEmojiCaption");
        emojiStyle.Elements()
            .Single(element => element.Name.LocalName == "Setter"
                && Attribute(element, "Property") == "FontFamily")
            .Attribute("Value")!.Value.Should().Be("{StaticResource EmojiFontFamily}");
    }

    private static XDocument LoadXaml(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "Client", .. segments]);
        File.Exists(path).Should().BeTrue($"the test project must copy {path} to its output");
        return XDocument.Load(path);
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
}
