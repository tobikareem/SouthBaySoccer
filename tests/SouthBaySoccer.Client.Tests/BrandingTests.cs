using System.Xml.Linq;
using FluentAssertions;

namespace SouthBaySoccer.Client.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void ShippedClientBranding_UsesN9jaBay()
    {
        var shell = Load("Xaml", "AppShell.xaml");
        var welcome = Load("Xaml", "WelcomeBackPage.xaml");
        var project = Load("Source", "SouthBaySoccer.csproj");

        shell.Root!.Attribute("Title")!.Value.Should().Be("N9ja Bay");
        welcome.Descendants()
            .Any(element => element.Name.LocalName == "Label"
                && element.Attribute("Text")?.Value == "N9ja Bay")
            .Should().BeTrue();
        project.Descendants("ApplicationTitle").Single().Value.Should().Be("N9ja Bay");
    }

    private static XDocument Load(string directory, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Client", directory, fileName);

        return XDocument.Load(path);
    }
}
