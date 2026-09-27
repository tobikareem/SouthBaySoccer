using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

/// <summary>
/// Guards every <c>{StaticResource}</c> in the app's XAML against a key that is never defined.
/// <para>
/// Keys in app-level merged dictionaries resolve at runtime, not at compile time, so an undefined
/// one builds clean and then throws while Shell inflates the page. On the startup page that is a
/// launch crash reachable only on a device, which is how it escaped to TestFlight once already.
/// </para>
/// </summary>
public class StaticResourceKeyTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";

    // Matches "{StaticResource Foo}" wherever it appears — standalone, or nested inside an
    // AppThemeBinding/Setter where the whole markup extension sits in one attribute value.
    private static readonly Regex StaticResourceReference = new(
        @"\{\s*StaticResource\s+([^},\s]+)\s*\}",
        RegexOptions.Compiled);

    [Fact]
    public void StaticResourceReferences_AcrossAllAppXaml_AreAllDefined()
    {
        var files = XamlFiles();
        files.Should().NotBeEmpty("the csproj mirrors every app XAML file into Client/XamlAll");

        var definedKeys = files.SelectMany(DefinedKeys).ToHashSet(StringComparer.Ordinal);
        var undefined = files
            .SelectMany(file => ReferencedKeys(file).Select(key => (File: Path.GetFileName(file), Key: key)))
            .Where(reference => !definedKeys.Contains(reference.Key))
            .Select(reference => $"{reference.File} -> {{StaticResource {reference.Key}}}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Joined into the reason rather than left to the collection assertion: FluentAssertions
        // reports only the first offender, and fixing these one test run at a time is miserable.
        undefined.Should().BeEmpty(
            "every StaticResource key must be defined in a merged dictionary or a local " +
            "ResourceDictionary, or the page throws when it is inflated at runtime. Undefined:\n" +
            string.Join("\n", undefined));
    }

    [Fact]
    public void BadgeAndCardSurfaceKeys_UsedByTheStartupPage_AreDefined()
    {
        // Pinned explicitly: these two reached TestFlight undefined and crashed SessionsHomePage,
        // the Shell's first tab, during viewDidLoad.
        var definedKeys = XamlFiles().SelectMany(DefinedKeys).ToHashSet(StringComparer.Ordinal);

        definedKeys.Should().Contain("BrandWhite");
        definedKeys.Should().Contain("BrandPineDark");
    }

    private static string[] XamlFiles() =>
        Directory.Exists(XamlRoot())
            ? Directory.GetFiles(XamlRoot(), "*.xaml", SearchOption.AllDirectories)
            : [];

    private static string XamlRoot() =>
        Path.Combine(AppContext.BaseDirectory, "Client", "XamlAll");

    private static IEnumerable<string> DefinedKeys(string file) =>
        XDocument.Load(file)
            .Descendants()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!);

    private static IEnumerable<string> ReferencedKeys(string file) =>
        StaticResourceReference
            .Matches(File.ReadAllText(file))
            .Select(match => match.Groups[1].Value);
}
